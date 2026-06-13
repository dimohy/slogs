$ErrorActionPreference = 'Stop'

$container = 'slogs-postgres'

$podman = Get-Command podman -ErrorAction SilentlyContinue
if (-not $podman) {
    throw 'podman is not installed or is not on PATH.'
}

podman container exists $container 2>$null
if ($LASTEXITCODE -eq 0) {
    podman stop $container | Out-Null
    "Stopped $container"
} else {
    "$container does not exist."
}
