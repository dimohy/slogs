[CmdletBinding()]
param(
    [string]$RemoteHost = "maum.in",
    [string]$RemoteUser = "service",
    [string]$RemoteRoot = "/home/service/apps/slogs",
    [string]$Container = "slogs-postgres",
    [string]$Database = "slogs",
    [string]$DatabaseUser = "slogs",
    [string]$Label = "manual",
    [string]$LocalBackupRoot = "P:\Backups\Slogs"
)

$ErrorActionPreference = "Stop"

foreach ($value in @($RemoteHost, $RemoteUser, $Container, $Database, $DatabaseUser, $Label)) {
    if ($value -notmatch '^[A-Za-z0-9._-]+$') {
        throw "Unsafe backup argument: $value"
    }
}
if ($RemoteRoot -notmatch '^/[A-Za-z0-9._/-]+$') {
    throw "Unsafe remote root: $RemoteRoot"
}

$remoteScript = @'
set -eu

remote_root="$1"
container="$2"
database="$3"
database_user="$4"
label="$5"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_dir="$remote_root/backups"
backup_name="llm-wiki-${label}-${stamp}"
backup_path="$backup_dir/$backup_name.dump"
temporary_path="$backup_path.partial"
restore_database="slogs_restore_verify_${stamp}"
restore_database="$(printf '%s' "$restore_database" | tr '[:upper:]' '[:lower:]')"

mkdir -p "$backup_dir"
umask 077

cleanup() {
    docker exec "$container" dropdb -U "$database_user" --if-exists "$restore_database" >/dev/null 2>&1 || true
    rm -f "$temporary_path"
}
trap cleanup EXIT INT TERM

docker exec "$container" pg_dump -U "$database_user" -d "$database" -Fc > "$temporary_path"
test -s "$temporary_path"
docker exec -i "$container" pg_restore --list < "$temporary_path" >/dev/null
mv "$temporary_path" "$backup_path"
sha256sum "$backup_path" > "$backup_path.sha256"

production_counts="$(docker exec "$container" psql -U "$database_user" -d "$database" -At -F '|' -c '
SELECT COUNT(*) FROM "LlmWikiEntries"
UNION ALL SELECT COUNT(*) FROM "LlmWikiEntrySources"
UNION ALL SELECT COUNT(*) FROM "LlmWikiEntryEmbeddings"
UNION ALL SELECT COUNT(*) FROM "LlmWikiEntryGraphNodes";
')"

docker exec "$container" createdb -U "$database_user" "$restore_database"
docker exec -i "$container" pg_restore -U "$database_user" -d "$restore_database" --no-owner --no-privileges < "$backup_path"

restored_counts="$(docker exec "$container" psql -U "$database_user" -d "$restore_database" -At -F '|' -c '
SELECT COUNT(*) FROM "LlmWikiEntries"
UNION ALL SELECT COUNT(*) FROM "LlmWikiEntrySources"
UNION ALL SELECT COUNT(*) FROM "LlmWikiEntryEmbeddings"
UNION ALL SELECT COUNT(*) FROM "LlmWikiEntryGraphNodes";
')"

if [ "$production_counts" != "$restored_counts" ]; then
    printf 'Production counts:\n%s\nRestored counts:\n%s\n' "$production_counts" "$restored_counts" >&2
    exit 1
fi

checksum="$(cut -d ' ' -f 1 "$backup_path.sha256")"
size_bytes="$(wc -c < "$backup_path" | tr -d ' ')"
counts_csv="$(printf '%s' "$production_counts" | paste -sd, -)"
manifest_path="$backup_path.manifest"
{
    printf 'format=postgresql-custom\n'
    printf 'database=%s\n' "$database"
    printf 'createdUtc=%s\n' "$stamp"
    printf 'sha256=%s\n' "$checksum"
    printf 'sizeBytes=%s\n' "$size_bytes"
    printf 'counts=entries,sources,embeddings,graphNodes:%s\n' "$counts_csv"
    printf 'archiveListValidated=true\n'
    printf 'restoreDrillValidated=true\n'
} > "$manifest_path"

docker exec "$container" dropdb -U "$database_user" "$restore_database"
trap - EXIT INT TERM

printf 'BACKUP_PATH=%s\n' "$backup_path"
printf 'CHECKSUM=%s\n' "$checksum"
printf 'SIZE_BYTES=%s\n' "$size_bytes"
printf 'COUNTS=%s\n' "$counts_csv"
printf 'RESTORE_DRILL=PASS\n'
'@

$remoteTarget = "$RemoteUser@$RemoteHost"
$remoteCommand = "bash -s -- '$RemoteRoot' '$Container' '$Database' '$DatabaseUser' '$Label'"
$remoteOutput = $remoteScript | & ssh -o BatchMode=yes $remoteTarget $remoteCommand
if ($LASTEXITCODE -ne 0) {
    throw "Remote database backup or restore drill failed with exit code $LASTEXITCODE."
}

$result = @{}
foreach ($line in $remoteOutput) {
    if ($line -match '^([A-Z_]+)=(.*)$') {
        $result[$Matches[1]] = $Matches[2]
    }
}
foreach ($required in @("BACKUP_PATH", "CHECKSUM", "SIZE_BYTES", "COUNTS", "RESTORE_DRILL")) {
    if (-not $result.ContainsKey($required)) {
        throw "Backup output is missing $required."
    }
}
if ($result.RESTORE_DRILL -ne "PASS") {
    throw "The remote restore drill did not pass."
}

$backupPath = $result.BACKUP_PATH
$backupName = [IO.Path]::GetFileName($backupPath)
$resolvedLocalRoot = [IO.Path]::GetFullPath($LocalBackupRoot)
New-Item -ItemType Directory -Force -Path $resolvedLocalRoot | Out-Null

foreach ($suffix in @("", ".sha256", ".manifest")) {
    & scp "${remoteTarget}:${backupPath}${suffix}" (Join-Path $resolvedLocalRoot "${backupName}${suffix}")
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to copy ${backupName}${suffix} to the local backup root."
    }
}

$localBackupPath = Join-Path $resolvedLocalRoot $backupName
$localChecksum = (Get-FileHash -Algorithm SHA256 -LiteralPath $localBackupPath).Hash.ToLowerInvariant()
if ($localChecksum -ne $result.CHECKSUM) {
    throw "The local backup checksum differs from the verified remote backup."
}

[pscustomobject]@{
    RemotePath = $backupPath
    LocalPath = $localBackupPath
    Sha256 = $localChecksum
    SizeBytes = [long]$result.SIZE_BYTES
    Counts = $result.COUNTS
    ArchiveListValidated = $true
    RestoreDrillValidated = $true
}
