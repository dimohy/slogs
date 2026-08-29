$ErrorActionPreference = 'Stop'

$container = 'slogs-bge-m3-full'
$volume = 'slogs-bge-m3-full-data'
$image = 'localhost/slogs-bge-m3-full:1.0.0'
$model = 'BAAI/bge-m3'
$revision = '5617a9f61b028005a4858fdac845db406aefb181'
$modelPath = '/models/bge-m3'
$port = '8082'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $repoRoot 'infra\bge-m3-full'

$podman = Get-Command podman -ErrorAction SilentlyContinue
if (-not $podman) {
    throw 'podman is not installed or is not on PATH.'
}

$machineList = podman machine list --format json 2>$null | ConvertFrom-Json
if ($machineList -and ($machineList | Where-Object { $_.Running -eq $false })) {
    podman machine start | Out-Null
}

podman build --tag $image $buildRoot
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to build the BGE-M3 full-function runtime image.'
}

podman volume exists $volume 2>$null
if ($LASTEXITCODE -ne 0) {
    podman volume create $volume | Out-Null
}

podman run --rm -v "${volume}:/models" --entrypoint python $image -c `
    "from huggingface_hub import snapshot_download; snapshot_download('$model', revision='$revision', local_dir='$modelPath')"
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to materialize the revision-locked BGE-M3 model.'
}

podman container exists $container 2>$null
if ($LASTEXITCODE -eq 0) {
    podman rm -f $container | Out-Null
}
$runOutput = podman run -d --name $container `
    --device nvidia.com/gpu=all `
    -e "BGE_M3_MODEL_PATH=$modelPath" `
    -e "BGE_M3_MODEL_REVISION=$revision" `
    -e "BGE_M3_ENCODE_BATCH_SIZE=1" `
    -e "BGE_M3_SCORE_BATCH_SIZE=8" `
    -e "BGE_M3_ENCODE_LOCK_SLICE_SIZE=4" `
    -p "${port}:8080" `
    -v "${volume}:/models" `
    $image 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Failed to start the BGE-M3 full-function runtime. $runOutput"
}

$candidateBaseUrls = [System.Collections.Generic.List[string]]::new()
$candidateBaseUrls.Add("http://localhost:${port}")
$wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
if ($wsl) {
    $addressLines = wsl.exe -d podman-machine-default sh -lc "/usr/sbin/ip -o -4 addr show eth0" 2>$null
    foreach ($addressLine in $addressLines) {
        if ($addressLine -match '\binet\s+(?<address>\d+\.\d+\.\d+\.\d+)/') {
            $candidateBaseUrls.Add("http://$($Matches.address):${port}")
        }
    }
}
$ready = $false
$runtimeBaseUrl = $null
for ($i = 0; $i -lt 180; $i++) {
    foreach ($candidateBaseUrl in $candidateBaseUrls) {
        try {
            $health = Invoke-WebRequest -Uri "${candidateBaseUrl}/health" -TimeoutSec 2
            if ($health.StatusCode -eq 200) {
                $runtimeBaseUrl = $candidateBaseUrl
                $ready = $true
                break
            }
        } catch {
            # Readiness failures are retried until the explicit deadline below.
        }
    }
    if ($ready) {
        break
    }
    Start-Sleep -Seconds 1
}

if (-not $ready) {
    podman logs --tail 100 $container
    throw 'BGE-M3 full-function runtime did not become ready.'
}

$info = Invoke-RestMethod -Uri "${runtimeBaseUrl}/info"
if ($info.modelId -ne $model -or $info.modelRevision -ne $revision -or
    $info.dimensions -ne 1024 -or $info.maxInputTokens -ne 8192 -or
    $info.encodeBatchSize -ne 1 -or $info.scoreBatchSize -ne 8 -or $info.encodeLockSliceSize -ne 4 -or $info.concurrentGpuRequests -ne 1) {
    throw "BGE-M3 full-function runtime contract drift: $($info | ConvertTo-Json -Compress)"
}
$requiredFunctions = 'dense', 'sparse', 'multi-vector', 'pair-score'
foreach ($function in $requiredFunctions) {
    if ($info.functions -notcontains $function) {
        throw "BGE-M3 full-function runtime is missing $function."
    }
}

[pscustomobject]@{ BaseUrl = $runtimeBaseUrl; Runtime = $info } | ConvertTo-Json -Depth 8
podman ps --filter "name=$container" --format "table {{.ID}}\t{{.Image}}\t{{.Names}}\t{{.Status}}\t{{.Ports}}"
