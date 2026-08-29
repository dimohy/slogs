$ErrorActionPreference = 'Stop'

$container = 'slogs-bge-m3-full'
podman container exists $container 2>$null
if ($LASTEXITCODE -eq 0) {
    podman stop $container
}
