[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CorpusDirectory,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$RemoteHost = "maum.in",
    [string]$RemoteUser = "service",
    [string]$RemoteRoot = "/home/service/apps/slogs",
    [string]$LocalBackupRoot = "P:\Backups\Slogs"
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^[A-Za-z0-9._-]+$') { throw "Version contains unsafe characters." }
foreach ($value in @($RemoteHost, $RemoteUser)) {
    if ($value -notmatch '^[A-Za-z0-9._-]+$') { throw "Unsafe remote argument: $value" }
}
if ($RemoteRoot -notmatch '^/[A-Za-z0-9._/-]+$') { throw "Unsafe remote root: $RemoteRoot" }

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedCorpus = (Resolve-Path -LiteralPath $CorpusDirectory).Path
$resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath).Path
$manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json
if (-not $resolvedManifest.StartsWith($resolvedCorpus + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "ManifestPath must be inside CorpusDirectory so the exact validated corpus is deployed."
}
foreach ($required in @("corpus-manifest.json", "entries.jsonl", "sources.jsonl")) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedCorpus $required) -PathType Leaf)) {
        throw "Semantic corpus is missing $required."
    }
}

$backupScript = Join-Path $PSScriptRoot "Backup-SlogsDatabase.ps1"
$backup = & $backupScript `
    -RemoteHost $RemoteHost `
    -RemoteUser $RemoteUser `
    -RemoteRoot $RemoteRoot `
    -Label "semantic-$Version" `
    -LocalBackupRoot $LocalBackupRoot
if (-not $backup.RestoreDrillValidated) {
    throw "Semantic activation requires a production backup that passed a restore drill."
}

$artifactRoot = Join-Path $repoRoot "artifacts\llm-wiki\semantic-deploy"
[IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
$archivePath = Join-Path $artifactRoot "semantic-$Version-$([Guid]::NewGuid().ToString('N')).tar.gz"
$archiveName = [IO.Path]::GetFileName($archivePath)
$remoteArchive = "$RemoteRoot/$archiveName"
$remoteTarget = "$RemoteUser@$RemoteHost"

try {
    & tar -czf $archivePath -C $resolvedCorpus .
    if ($LASTEXITCODE -ne 0) { throw "Failed to archive the semantic corpus." }
    & scp $archivePath "${remoteTarget}:$remoteArchive"
    if ($LASTEXITCODE -ne 0) { throw "Failed to transfer the semantic corpus." }

    $manifestRelativePath = [IO.Path]::GetRelativePath($resolvedCorpus, $resolvedManifest).Replace('\', '/')
    $remoteScript = @'
set -eu
remote_root="$1"
archive_path="$2"
version="$3"
manifest_relative="$4"

case "$archive_path" in
  "$remote_root"/semantic-*.tar.gz) ;;
  *) echo "Unsafe semantic archive path" >&2; exit 1 ;;
esac

current_release="$(readlink -f "$remote_root/current")"
case "$current_release" in
  "$remote_root"/releases/*) ;;
  *) echo "Current release resolves outside the release root" >&2; exit 1 ;;
esac

import_dir="$current_release/.semantic-import-$version"
case "$import_dir" in
  "$remote_root"/releases/*/.semantic-import-*) ;;
  *) echo "Unsafe semantic import directory" >&2; exit 1 ;;
esac
test ! -e "$import_dir"
umask 077
mkdir "$import_dir"
cleanup() {
  case "$import_dir" in "$remote_root"/releases/*/.semantic-import-*) rm -rf -- "$import_dir" ;; esac
  rm -f -- "$archive_path"
}
trap cleanup EXIT INT TERM
tar -xzf "$archive_path" -C "$import_dir"
test -f "$import_dir/corpus-manifest.json"
test -f "$import_dir/entries.jsonl"
test -f "$import_dir/sources.jsonl"
test -f "$import_dir/$manifest_relative"

cd "$remote_root"
docker compose --env-file "$remote_root/.env" run --rm --no-deps app \
  /app/Slogs \
  --llm-wiki-semantic-import "/app/.semantic-import-$version/$manifest_relative" \
  --semantic-corpus "/app/.semantic-import-$version" \
  --semantic-version "$version" \
  --activate-semantic-graph

counts="$(docker exec slogs-postgres psql -U slogs -d slogs -X -q -A -t -F ',' -v ON_ERROR_STOP=1 -v version="$version" -c '
SELECT
  (SELECT COUNT(*) FROM "LlmWikiSemanticEntities" WHERE "Version"=:'"'"'version'"'"'),
  (SELECT COUNT(*) FROM "LlmWikiSemanticMentions" WHERE "Version"=:'"'"'version'"'"'),
  (SELECT COUNT(*) FROM "LlmWikiSemanticRelations" WHERE "Version"=:'"'"'version'"'"'),
  (SELECT COUNT(*) FROM "LlmWikiMemorySplitProposals" WHERE "Version"=:'"'"'version'"'"'),
  (SELECT COUNT(*) FROM "LlmWikiSemanticGraphVersions" WHERE "Version"=:'"'"'version'"'"' AND "State"='"'"'active'"'"');
')"
printf 'SEMANTIC_DEPLOY=PASS version=%s counts=%s\n' "$version" "$counts"
'@
    $remoteCommand = "bash -s -- '$RemoteRoot' '$remoteArchive' '$Version' '$manifestRelativePath'"
    $null = $remoteScript | & ssh -o BatchMode=yes $remoteTarget $remoteCommand
    if ($LASTEXITCODE -ne 0) { throw "Remote semantic activation failed." }
    $verificationScript = @'
set -eu
version="$1"
query="SELECT
  (SELECT COUNT(*) FROM \"LlmWikiSemanticEntities\" WHERE \"Version\"='${version}'),
  (SELECT COUNT(*) FROM \"LlmWikiSemanticMentions\" WHERE \"Version\"='${version}'),
  (SELECT COUNT(*) FROM \"LlmWikiSemanticRelations\" WHERE \"Version\"='${version}'),
  (SELECT COUNT(*) FROM \"LlmWikiMemorySplitProposals\" WHERE \"Version\"='${version}'),
  (SELECT COUNT(*) FROM \"LlmWikiSemanticGraphVersions\" WHERE \"Version\"='${version}' AND \"State\"='active');"
docker exec slogs-postgres psql -U slogs -d slogs -X -q -A -t -F ',' -v ON_ERROR_STOP=1 -c "$query"
'@
    $verifiedCounts = @($verificationScript | & ssh -o BatchMode=yes $remoteTarget "bash -s -- '$Version'")
    if ($LASTEXITCODE -ne 0) { throw "Remote semantic activation verification failed." }
    $expectedCounts = "$($manifest.entities.Count),$($manifest.mentions.Count),$($manifest.relations.Count),$($manifest.splitProposals.Count),1"
    $matchingCounts = @($verifiedCounts | Where-Object { $_.Trim() -eq $expectedCounts })
    if ($matchingCounts.Count -ne 1) {
        throw "Remote semantic activation counts differ from the manifest. Expected $expectedCounts."
    }

    [pscustomobject]@{
        Version = $Version
        ManifestSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedManifest).Hash.ToLowerInvariant()
        BackupRemotePath = $backup.RemotePath
        BackupLocalPath = $backup.LocalPath
        BackupSha256 = $backup.Sha256
        BackupRestoreDrillValidated = $backup.RestoreDrillValidated
        Result = "SEMANTIC_DEPLOY=PASS version=$Version counts=$expectedCounts"
    }
}
finally {
    if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
        Remove-Item -LiteralPath $archivePath -Force
    }
}
