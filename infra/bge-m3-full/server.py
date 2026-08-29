import os
import threading
from contextlib import asynccontextmanager

import numpy as np
import torch
from fastapi import FastAPI, HTTPException
from FlagEmbedding import BGEM3FlagModel
from pydantic import BaseModel, Field


MODEL_PATH = os.environ["BGE_M3_MODEL_PATH"]
MODEL_REVISION = os.environ["BGE_M3_MODEL_REVISION"]
MODEL_ID = "BAAI/bge-m3"
ENCODE_BATCH_SIZE = int(os.environ.get("BGE_M3_ENCODE_BATCH_SIZE", "1"))
SCORE_BATCH_SIZE = int(os.environ.get("BGE_M3_SCORE_BATCH_SIZE", "8"))
if ENCODE_BATCH_SIZE < 1 or ENCODE_BATCH_SIZE > 32:
    raise RuntimeError("BGE_M3_ENCODE_BATCH_SIZE must be between 1 and 32.")
if SCORE_BATCH_SIZE < 1 or SCORE_BATCH_SIZE > 32:
    raise RuntimeError("BGE_M3_SCORE_BATCH_SIZE must be between 1 and 32.")
model: BGEM3FlagModel | None = None
model_lock = threading.Lock()


class EncodeRequest(BaseModel):
    inputs: list[str] = Field(min_length=1, max_length=256)
    return_dense: bool = True
    return_sparse: bool = True
    return_multi_vector: bool = False
    max_length: int = Field(default=8192, ge=1, le=8192)


class ScoreRequest(BaseModel):
    pairs: list[tuple[str, str]] = Field(min_length=1, max_length=256)
    weights: tuple[float, float, float]
    max_query_length: int = Field(default=512, ge=1, le=8192)
    max_passage_length: int = Field(default=8192, ge=1, le=8192)


@asynccontextmanager
async def lifespan(_: FastAPI):
    global model
    if not torch.cuda.is_available():
        raise RuntimeError("BGE-M3 full-function runtime requires CUDA.")
    model = BGEM3FlagModel(MODEL_PATH, use_fp16=True)
    yield
    model = None


app = FastAPI(lifespan=lifespan)


def to_json_value(value):
    if isinstance(value, np.ndarray):
        return value.tolist()
    if isinstance(value, np.generic):
        return value.item()
    if isinstance(value, dict):
        return {str(key): to_json_value(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [to_json_value(item) for item in value]
    return value


@app.get("/health")
def health():
    if model is None:
        raise HTTPException(status_code=503, detail="model is not ready")
    return {"status": "ok"}


@app.get("/info")
def info():
    return {
        "modelId": MODEL_ID,
        "modelRevision": MODEL_REVISION,
        "runtime": "FlagEmbedding",
        "runtimeVersion": "1.4.2",
        "dimensions": 1024,
        "maxInputTokens": 8192,
        "encodeBatchSize": ENCODE_BATCH_SIZE,
        "scoreBatchSize": SCORE_BATCH_SIZE,
        "concurrentGpuRequests": 1,
        "functions": ["dense", "sparse", "multi-vector", "pair-score"],
        "cudaDevice": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,
    }


@app.post("/encode")
def encode(request: EncodeRequest):
    if model is None:
        raise HTTPException(status_code=503, detail="model is not ready")
    if not request.return_dense and not request.return_sparse and not request.return_multi_vector:
        raise HTTPException(status_code=400, detail="at least one output function is required")
    with model_lock:
        output = model.encode(
            request.inputs,
            batch_size=min(ENCODE_BATCH_SIZE, len(request.inputs)),
            max_length=request.max_length,
            return_dense=request.return_dense,
            return_sparse=request.return_sparse,
            return_colbert_vecs=request.return_multi_vector,
        )
    response = {}
    if request.return_dense:
        response["dense"] = output["dense_vecs"].tolist()
    if request.return_sparse:
        response["sparse"] = to_json_value(output["lexical_weights"])
    if request.return_multi_vector:
        response["multiVector"] = [value.tolist() for value in output["colbert_vecs"]]
    return response


@app.post("/score")
def score(request: ScoreRequest):
    if model is None:
        raise HTTPException(status_code=503, detail="model is not ready")
    if any(value < 0 for value in request.weights) or sum(request.weights) <= 0:
        raise HTTPException(status_code=400, detail="weights must be non-negative with a positive sum")
    with model_lock:
        result = model.compute_score(
            request.pairs,
            batch_size=min(SCORE_BATCH_SIZE, len(request.pairs)),
            max_query_length=request.max_query_length,
            max_passage_length=request.max_passage_length,
            weights_for_different_modes=list(request.weights),
        )
    return to_json_value(result)
