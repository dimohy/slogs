$ErrorActionPreference = 'Stop'

$container = 'slogs-postgres'
$volume = 'slogs-postgres-data'
$database = 'slogs'
$user = 'slogs'
$password = 'slogs_dev_password'
$port = '54329'

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
    $state = podman inspect $container --format '{{.State.Status}}'
    if ($state -ne 'running') {
        podman start $container | Out-Null
    }
} else {
    podman run -d --name $container `
        -e POSTGRES_DB=$database `
        -e POSTGRES_USER=$user `
        -e POSTGRES_PASSWORD=$password `
        -p "${port}:5432" `
        -v "${volume}:/var/lib/postgresql/data" `
        docker.io/library/postgres:16 | Out-Null
}

$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    podman exec $container pg_isready -U $user -d $database | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $ready = $true
        break
    }

    Start-Sleep -Seconds 1
}

if (-not $ready) {
    throw 'PostgreSQL container did not become ready.'
}

podman ps --filter "name=$container" --format "table {{.ID}}\t{{.Names}}\t{{.Status}}\t{{.Ports}}"
