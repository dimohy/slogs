$ErrorActionPreference = 'Stop'

$container = 'slogs-embeddinggemma'
$volume = 'slogs-embeddinggemma-data'
$image = 'docker.io/ollama/ollama:0.11.10'
$model = 'embeddinggemma'
$port = '11434'

$podman = Get-Command podman -ErrorAction SilentlyContinue
if (-not $podman) {
    throw 'podman is not installed or is not on PATH.'
}

$machineList = podman machine list --format json 2>$null | ConvertFrom-Json
if ($machineList -and ($machineList | Where-Object { $_.Running -eq $false })) {
    podman machine start | Out-Null
}

podman volume exists $volume 2>$null
if ($LASTEXITCODE -ne 0) {
    podman volume create $volume | Out-Null
}

podman container exists $container 2>$null
if ($LASTEXITCODE -eq 0) {
    $currentImage = podman inspect $container --format '{{.Config.Image}}'
    $gpuMarker = podman inspect $container --format '{{range .Config.Env}}{{println .}}{{end}}' | Select-String -Pattern '^SLOGS_EMBEDDINGGEMMA_GPU=required$' -Quiet
    if ($currentImage -ne $image -or -not $gpuMarker) {
        podman rm -f $container | Out-Null
    }
}

podman container exists $container 2>$null
if ($LASTEXITCODE -eq 0) {
    $state = podman inspect $container --format '{{.State.Status}}'
    if ($state -ne 'running') {
        podman start $container | Out-Null
    }
} else {
    $runOutput = podman run -d --name $container `
        --device nvidia.com/gpu=all `
        -e SLOGS_EMBEDDINGGEMMA_GPU=required `
        -p "${port}:11434" `
        -v "${volume}:/root/.ollama" `
        $image 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start EmbeddingGemma with NVIDIA GPU CDI device nvidia.com/gpu=all. $runOutput"
    }
}

$ready = $false
for ($i = 0; $i -lt 120; $i++) {
    podman exec $container ollama list 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $ready = $true
        break
    }

    Start-Sleep -Seconds 1
}

if (-not $ready) {
    throw 'EmbeddingGemma model server did not become ready.'
}

podman exec $container ollama pull $model
podman exec $container ollama list
podman ps --filter "name=$container" --format "table {{.ID}}\t{{.Image}}\t{{.Names}}\t{{.Status}}\t{{.Ports}}"
