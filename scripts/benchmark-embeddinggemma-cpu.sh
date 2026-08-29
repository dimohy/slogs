#!/usr/bin/env bash
set -euo pipefail

container_name="slogs-embeddinggemma-cpu-benchmark"
model_storage="${SLOGS_EMBEDDINGGEMMA_STORAGE:-/home/service/apps/slogs/embeddinggemma-data}"
image="ollama/ollama:0.11.10"
host_port="11435"
created=0

cleanup() {
    if [[ "$created" == "1" ]]; then
        docker stop --time 5 "$container_name" >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

if docker inspect "$container_name" >/dev/null 2>&1; then
    echo "Benchmark container already exists: $container_name" >&2
    exit 1
fi

if [[ ! -d "$model_storage" ]] && ! docker volume inspect "$model_storage" >/dev/null 2>&1; then
    echo "EmbeddingGemma model storage is missing: $model_storage" >&2
    exit 1
fi

docker run --rm -d \
    --name "$container_name" \
    --restart no \
    --cpus 8 \
    -e CUDA_VISIBLE_DEVICES=-1 \
    -e OLLAMA_LLM_LIBRARY=cpu_avx2 \
    -p "127.0.0.1:${host_port}:11434" \
    -v "${model_storage}:/root/.ollama" \
    "$image" >/dev/null
created=1

ready=0
for _ in $(seq 1 90); do
    if curl -fsS "http://127.0.0.1:${host_port}/api/tags" >/dev/null 2>&1; then
        ready=1
        break
    fi
    sleep 1
done
if [[ "$ready" != "1" ]]; then
    docker logs --tail 100 "$container_name" >&2 || true
    echo "EmbeddingGemma CPU benchmark runtime did not become ready." >&2
    exit 1
fi

python3 - "$host_port" <<'PY'
import json
import statistics
import sys
import time
import urllib.request

port = sys.argv[1]
queries = [
    "Slogs LLM Wiki에서 일반적인 프로젝트 결정을 찾아줘",
    "센서 배선 점검 절차",
    "사도행전 13장 9절",
    "회사의 전문 기술 문서에서 경고 원인을 찾아줘",
    "개인 기억의 최근 배포 판단 기준",
]

def embed(value):
    body = json.dumps({
        "model": "embeddinggemma",
        "input": value,
        "keep_alive": "5m",
    }).encode("utf-8")
    request = urllib.request.Request(
        f"http://127.0.0.1:{port}/api/embed",
        data=body,
        headers={"Content-Type": "application/json"},
    )
    started = time.perf_counter()
    with urllib.request.urlopen(request, timeout=180) as response:
        result = json.load(response)
    elapsed_ms = (time.perf_counter() - started) * 1000
    embeddings = result["embeddings"]
    if any(len(values) != 768 for values in embeddings):
        raise RuntimeError("Expected every embedding to contain 768 dimensions.")
    return elapsed_ms, len(embeddings)

cold_ms, _ = embed(f"task: search result | query: {queries[0]}")
warm_ms = [embed(f"task: search result | query: {query}")[0] for query in queries]
sample_document = ("title: 기술 문서 | category: company/manual | tags: sensor, safety\n"
                   "센서 배선과 전원 상태를 확인하고 반복 경고의 발생 순서와 측정값을 기록한다. " * 18)
document_batch_ms, document_count = embed([sample_document] * 20)
print(json.dumps({
    "runtime": "embeddinggemma-cpu",
    "dimensions": 768,
    "coldMs": round(cold_ms, 2),
    "warmMs": [round(value, 2) for value in warm_ms],
    "warmMeanMs": round(statistics.mean(warm_ms), 2),
    "warmP95Ms": round(sorted(warm_ms)[-1], 2),
    "documentBatchCount": document_count,
    "documentBatchMs": round(document_batch_ms, 2),
    "documentMeanMs": round(document_batch_ms / document_count, 2),
}, ensure_ascii=False))
PY

docker stats --no-stream --format \
    '{"container":"{{.Name}}","cpu":"{{.CPUPerc}}","memory":"{{.MemUsage}}"}' \
    "$container_name"
