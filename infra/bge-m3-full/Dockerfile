FROM docker.io/pytorch/pytorch:2.9.0-cuda12.8-cudnn9-runtime@sha256:f0ca81b440e252399d9954a45b616ee2540959466aacf3dfc3f856691eee66e8

ARG FLAG_EMBEDDING_VERSION=1.4.2
ARG FASTAPI_VERSION=0.141.1
ARG UVICORN_VERSION=0.52.4
ARG TRANSFORMERS_VERSION=5.16.1
ARG HUGGINGFACE_HUB_VERSION=1.29.0
ARG TOKENIZERS_VERSION=0.23.1
ARG SENTENCE_TRANSFORMERS_VERSION=6.0.0
ARG ACCELERATE_VERSION=1.14.0
ARG PEFT_VERSION=0.20.0

RUN python -m pip install --no-cache-dir \
    "FlagEmbedding==${FLAG_EMBEDDING_VERSION}" \
    "fastapi==${FASTAPI_VERSION}" \
    "uvicorn==${UVICORN_VERSION}" \
    "transformers==${TRANSFORMERS_VERSION}" \
    "huggingface-hub==${HUGGINGFACE_HUB_VERSION}" \
    "tokenizers==${TOKENIZERS_VERSION}" \
    "sentence-transformers==${SENTENCE_TRANSFORMERS_VERSION}" \
    "accelerate==${ACCELERATE_VERSION}" \
    "peft==${PEFT_VERSION}"

WORKDIR /app
COPY server.py /app/server.py

EXPOSE 8080
ENTRYPOINT ["python", "-m", "uvicorn", "server:app", "--host", "0.0.0.0", "--port", "8080"]
