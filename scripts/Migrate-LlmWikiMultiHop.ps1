[CmdletBinding()]
param(
    [string]$RemoteHost = "maum.in",
    [string]$RemoteUser = "service",
    [string]$RemoteRoot = "/home/service/apps/slogs",
    [string]$Container = "slogs-postgres",
    [string]$Database = "slogs",
    [string]$DatabaseUser = "slogs",
    [string]$LocalBackupRoot = "P:\Backups\Slogs"
)

$ErrorActionPreference = "Stop"

$backupScript = Join-Path $PSScriptRoot "Backup-SlogsDatabase.ps1"
$backup = & $backupScript `
    -RemoteHost $RemoteHost `
    -RemoteUser $RemoteUser `
    -RemoteRoot $RemoteRoot `
    -Container $Container `
    -Database $Database `
    -DatabaseUser $DatabaseUser `
    -Label "pre-multihop-index" `
    -LocalBackupRoot $LocalBackupRoot

if (-not $backup.RestoreDrillValidated) {
    throw "The migration requires a backup that passed a restore drill."
}

foreach ($value in @($RemoteHost, $RemoteUser, $Container, $Database, $DatabaseUser)) {
    if ($value -notmatch '^[A-Za-z0-9._-]+$') {
        throw "Unsafe migration argument: $value"
    }
}

$remoteScript = @'
set -eu

container="$1"
database="$2"
database_user="$3"

snapshot_counts() {
    docker exec "$container" psql -U "$database_user" -d "$database" -At -c '
SELECT COUNT(*) FROM "LlmWikiEntries"
UNION ALL SELECT COUNT(*) FROM "LlmWikiEntrySources"
UNION ALL SELECT COUNT(*) FROM "LlmWikiEntryEmbeddings"
UNION ALL SELECT COUNT(*) FROM "LlmWikiEntryGraphNodes";
' | paste -sd, -
}

entries_hash() {
    docker exec "$container" psql -U "$database_user" -d "$database" -At -c '
COPY (
    SELECT "Id", "OwnerUserName", "Slug", "Title", "Summary", "SourcePrompt", "Content",
           "TagsJson", "CategoryPath", "CategoryDepth", "CreatedAt", "UpdatedAt",
           "IsPublic", "PublishedAt"
    FROM "LlmWikiEntries"
    ORDER BY "Id"
) TO STDOUT WITH (FORMAT csv, HEADER false);
' | sha256sum | cut -d ' ' -f 1
}

sources_hash() {
    docker exec "$container" psql -U "$database_user" -d "$database" -At -c '
COPY (
    SELECT "Id", "EntryId", "OwnerUserName", "Action", "Prompt", "Content", "Title",
           "Tags", "CategoryPath", "CreatedAt"
    FROM "LlmWikiEntrySources"
    ORDER BY "Id"
) TO STDOUT WITH (FORMAT csv, HEADER false);
' | sha256sum | cut -d ' ' -f 1
}

before_counts="$(snapshot_counts)"
before_entries_hash="$(entries_hash)"
before_sources_hash="$(sources_hash)"

docker exec "$container" psql -U "$database_user" -d "$database" -v ON_ERROR_STOP=1 -c '
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_LlmWikiEntryGraphNodes_Owner_NodeKey_EntryId_Covering"
ON "LlmWikiEntryGraphNodes" ("OwnerUserName", "NodeKey", "EntryId") INCLUDE ("Weight");
'
docker exec "$container" psql -U "$database_user" -d "$database" -v ON_ERROR_STOP=1 -c '
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_LlmWikiEntryGraphNodes_EntryId_Owner_NodeKey_Covering"
ON "LlmWikiEntryGraphNodes" ("EntryId", "OwnerUserName", "NodeKey") INCLUDE ("Weight");
'
docker exec "$container" psql -U "$database_user" -d "$database" -v ON_ERROR_STOP=1 -c '
CREATE TABLE IF NOT EXISTS "LlmWikiGraphNodeStatistics" (
    "OwnerUserName" character varying(80) NOT NULL,
    "NodeKey" character varying(180) NOT NULL,
    "EntryCount" integer NOT NULL,
    "IndexVersion" character varying(80) NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LlmWikiGraphNodeStatistics" PRIMARY KEY ("OwnerUserName", "NodeKey")
);
CREATE TABLE IF NOT EXISTS "LlmWikiGraphIndexStates" (
    "OwnerUserName" character varying(80) NOT NULL,
    "IndexVersion" character varying(80) NOT NULL,
    "SourceNodeCount" bigint NOT NULL,
    "BuiltAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LlmWikiGraphIndexStates" PRIMARY KEY ("OwnerUserName")
);
CREATE TABLE IF NOT EXISTS "LlmWikiGraphEdges" (
    "OwnerUserName" character varying(80) NOT NULL,
    "FromEntryId" uuid NOT NULL,
    "ToEntryId" uuid NOT NULL,
    "EdgeScore" double precision NOT NULL,
    "IndexVersion" character varying(80) NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LlmWikiGraphEdges" PRIMARY KEY ("OwnerUserName", "FromEntryId", "ToEntryId"),
    CONSTRAINT "FK_LlmWikiGraphEdges_FromEntry" FOREIGN KEY ("FromEntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_LlmWikiGraphEdges_ToEntry" FOREIGN KEY ("ToEntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_LlmWikiGraphEdges_Owner_From_Score_To"
ON "LlmWikiGraphEdges" ("OwnerUserName", "FromEntryId", "EdgeScore" DESC, "ToEntryId");
TRUNCATE TABLE "LlmWikiGraphEdges", "LlmWikiGraphNodeStatistics", "LlmWikiGraphIndexStates";
INSERT INTO "LlmWikiGraphNodeStatistics"
    ("OwnerUserName", "NodeKey", "EntryCount", "IndexVersion", "UpdatedAt")
SELECT
    "OwnerUserName", "NodeKey", COUNT(DISTINCT "EntryId")::integer,
    '"'"'2026-08-29-multihop-node-frequency-v1'"'"', NOW()
FROM "LlmWikiEntryGraphNodes"
GROUP BY "OwnerUserName", "NodeKey";
WITH scored_edges AS (
    SELECT
        source_nodes."OwnerUserName",
        source_nodes."EntryId" AS "FromEntryId",
        neighbor_nodes."EntryId" AS "ToEntryId",
        LEAST(SUM(
            LEAST(source_nodes."Weight", neighbor_nodes."Weight")
            / LN(2.0 + frequency."EntryCount")
        ), 1.0) AS "EdgeScore"
    FROM "LlmWikiEntryGraphNodes" AS source_nodes
    INNER JOIN "LlmWikiEntryGraphNodes" AS neighbor_nodes
        ON neighbor_nodes."OwnerUserName" = source_nodes."OwnerUserName"
       AND neighbor_nodes."NodeKey" = source_nodes."NodeKey"
       AND neighbor_nodes."EntryId" <> source_nodes."EntryId"
    INNER JOIN "LlmWikiGraphNodeStatistics" AS frequency
        ON frequency."OwnerUserName" = source_nodes."OwnerUserName"
       AND frequency."NodeKey" = source_nodes."NodeKey"
    GROUP BY source_nodes."OwnerUserName", source_nodes."EntryId", neighbor_nodes."EntryId"
), ranked_edges AS (
    SELECT *, ROW_NUMBER() OVER (
        PARTITION BY "OwnerUserName", "FromEntryId"
        ORDER BY "EdgeScore" DESC, "ToEntryId"
    ) AS edge_rank
    FROM scored_edges
)
INSERT INTO "LlmWikiGraphEdges"
    ("OwnerUserName", "FromEntryId", "ToEntryId", "EdgeScore", "IndexVersion", "UpdatedAt")
SELECT "OwnerUserName", "FromEntryId", "ToEntryId", "EdgeScore",
       '"'"'2026-08-29-multihop-node-frequency-v1'"'"', NOW()
FROM ranked_edges
WHERE edge_rank <= 4;
INSERT INTO "LlmWikiGraphIndexStates"
    ("OwnerUserName", "IndexVersion", "SourceNodeCount", "BuiltAt")
SELECT
    "OwnerUserName", '"'"'2026-08-29-multihop-node-frequency-v1'"'"', COUNT(*)::bigint, NOW()
FROM "LlmWikiEntryGraphNodes"
GROUP BY "OwnerUserName";
'
docker exec "$container" psql -U "$database_user" -d "$database" -v ON_ERROR_STOP=1 -c '
ANALYZE "LlmWikiEntryGraphNodes";
ANALYZE "LlmWikiGraphNodeStatistics";
ANALYZE "LlmWikiGraphEdges";
'

after_counts="$(snapshot_counts)"
after_entries_hash="$(entries_hash)"
after_sources_hash="$(sources_hash)"

test "$before_counts" = "$after_counts"
test "$before_entries_hash" = "$after_entries_hash"
test "$before_sources_hash" = "$after_sources_hash"

valid_indexes="$(docker exec "$container" psql -U "$database_user" -d "$database" -At -c '
SELECT indexrelid::regclass::text || '"'"'|'"'"' || indisvalid || '"'"'|'"'"' || indisready
FROM pg_index
WHERE indexrelid IN (
    '"'"'"IX_LlmWikiEntryGraphNodes_Owner_NodeKey_EntryId_Covering"'"'"'::regclass,
    '"'"'"IX_LlmWikiEntryGraphNodes_EntryId_Owner_NodeKey_Covering"'"'"'::regclass
)
ORDER BY indexrelid::regclass::text;
')"

if [ "$(printf '%s\n' "$valid_indexes" | grep -Ec '\|(t|true)\|(t|true)$')" -ne 2 ]; then
    printf 'Covering-index validation failed:\n%s\n' "$valid_indexes" >&2
    exit 1
fi

derived_counts="$(docker exec "$container" psql -U "$database_user" -d "$database" -At -F ',' -c '
SELECT
    (SELECT COUNT(*) FROM "LlmWikiGraphNodeStatistics"),
    (SELECT COUNT(*) FROM "LlmWikiGraphEdges"),
    (SELECT COUNT(*) FROM "LlmWikiGraphIndexStates"),
    COALESCE((
        SELECT MAX(edge_count)
        FROM (
            SELECT COUNT(*) AS edge_count
            FROM "LlmWikiGraphEdges"
            GROUP BY "OwnerUserName", "FromEntryId"
        ) AS degrees
    ), 0);
')"
IFS=',' read -r statistics_count edge_count state_count maximum_out_degree <<EOF
$derived_counts
EOF
if [ "$statistics_count" -lt 1 ] || [ "$edge_count" -lt 1 ] || [ "$state_count" -lt 1 ] || [ "$maximum_out_degree" -gt 4 ]; then
    printf 'Derived graph validation failed: %s\n' "$derived_counts" >&2
    exit 1
fi

printf 'COUNTS=%s\n' "$after_counts"
printf 'ENTRIES_HASH=%s\n' "$after_entries_hash"
printf 'SOURCES_HASH=%s\n' "$after_sources_hash"
printf 'INDEXES=%s\n' "$(printf '%s' "$valid_indexes" | paste -sd, -)"
printf 'DERIVED_COUNTS=%s\n' "$derived_counts"
printf 'MIGRATION=PASS\n'
'@

$remoteTarget = "$RemoteUser@$RemoteHost"
$remoteCommand = "bash -s -- '$Container' '$Database' '$DatabaseUser'"
$migrationOutput = $remoteScript | & ssh -o BatchMode=yes $remoteTarget $remoteCommand
if ($LASTEXITCODE -ne 0) {
    throw "The multi-hop index migration failed with exit code $LASTEXITCODE."
}

$result = @{}
foreach ($line in $migrationOutput) {
    if ($line -match '^([A-Z_]+)=(.*)$') {
        $result[$Matches[1]] = $Matches[2]
    }
}
if ($result.MIGRATION -ne "PASS") {
    throw "The migration did not return its PASS contract."
}

[pscustomobject]@{
    Migration = $result.MIGRATION
    Counts = $result.COUNTS
    EntriesHash = $result.ENTRIES_HASH
    SourcesHash = $result.SOURCES_HASH
    Indexes = $result.INDEXES
    DerivedCounts = $result.DERIVED_COUNTS
    BackupRemotePath = $backup.RemotePath
    BackupLocalPath = $backup.LocalPath
    BackupSha256 = $backup.Sha256
    BackupRestoreDrillValidated = $backup.RestoreDrillValidated
}
