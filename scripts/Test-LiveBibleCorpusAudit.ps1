[CmdletBinding()]
param(
    [string]$RemoteHost = "maum.in",
    [string]$RemoteUser = "service",
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

$sql = @'
WITH collections AS (
  SELECT c.*,
    (SELECT COUNT(*) FROM "LlmWikiKnowledgeChunks" k
     WHERE k."CollectionId"=c."CollectionId" AND k."Version"=c."Version" AND k."OwnerUserName"=c."OwnerUserName") AS chunks,
    (SELECT COUNT(*) FROM "LlmWikiKnowledgeEntities" e
     WHERE e."CollectionId"=c."CollectionId" AND e."Version"=c."Version" AND e."OwnerUserName"=c."OwnerUserName") AS entities,
    (SELECT COUNT(*) FROM "LlmWikiKnowledgeRelations" r
     WHERE r."CollectionId"=c."CollectionId" AND r."Version"=c."Version" AND r."OwnerUserName"=c."OwnerUserName") AS relations,
    (SELECT COUNT(*) FROM "LlmWikiKnowledgeCollectionAcl" a
     WHERE a."CollectionId"=c."CollectionId" AND a."Version"=c."Version" AND a."OwnerUserName"=c."OwnerUserName") AS acl_grants
  FROM "LlmWikiKnowledgeCollections" c
  WHERE c."Status"='active' AND c."CollectionId" IN
    ('bible-ko-nkrv','bible-ko-tkv','bible-original-step','bible-reviewed-relations')
), original AS (
  SELECT * FROM collections WHERE "CollectionId"='bible-original-step' AND "Version"='0.1.0'
), original_relations AS (
  SELECT r.* FROM "LlmWikiKnowledgeRelations" r JOIN original c
    ON c."CollectionId"=r."CollectionId" AND c."Version"=r."Version" AND c."OwnerUserName"=r."OwnerUserName"
)
SELECT json_build_object(
  'collections', (SELECT json_agg(json_build_object(
    'collectionId',"CollectionId",'version',"Version",'license',"License",
    'ownerKind',"OwnerKind",'ownerKey',"OwnerKey",'visibility',"Visibility",
    'redistributionAllowed',"RedistributionAllowed",'status',"Status",
    'chunks',chunks,'entities',entities,'relations',relations,'aclGrants',acl_grants
  ) ORDER BY "CollectionId","Version") FROM collections),
  'publicOriginalCandidateCount', (SELECT COUNT(*) FROM original_relations WHERE "ReviewStatus"='candidate'),
  'publicOriginalNonPublicSourceCount', (SELECT COUNT(*) FROM original_relations WHERE COALESCE("MetadataJson"->>'sourceVisibility','public_shared') <> 'public_shared'),
  'paulEntityCount', (SELECT COUNT(*) FROM "LlmWikiKnowledgeEntities" e JOIN original c
    ON c."CollectionId"=e."CollectionId" AND c."Version"=e."Version" AND c."OwnerUserName"=e."OwnerUserName"
    WHERE e."EntityId"='entity:step:G3972G' AND e."AliasesJson" ? 'Saul' AND e."MetadataJson"->>'strongIds' LIKE '%G4569G%'),
  'kingSaulEntityCount', (SELECT COUNT(*) FROM "LlmWikiKnowledgeEntities" e JOIN original c
    ON c."CollectionId"=e."CollectionId" AND c."Version"=e."Version" AND c."OwnerUserName"=e."OwnerUserName"
    WHERE e."EntityId"='entity:step:H7586G'),
  'paulActsMentionCount', (SELECT COUNT(*) FROM original_relations
    WHERE "RelationType"='mentions' AND "FromNodeId"='passage:Acts.13.9' AND "ToNodeId"='entity:step:G3972G'
      AND "ReviewStatus"='published'),
  'forbiddenKingSaulPaulMergeCount', (SELECT COUNT(*) FROM original_relations
    WHERE "RelationType" IN ('same_as','same_entity')
      AND (("FromNodeId"='entity:step:H7586G' AND "ToNodeId"='entity:step:G3972G')
        OR ("FromNodeId"='entity:step:G3972G' AND "ToNodeId"='entity:step:H7586G')))
);
'@

$remoteCommand = 'docker exec -i slogs-postgres sh -lc ''psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At'''
$raw = ($sql | & ssh -o BatchMode=yes "$RemoteUser@$RemoteHost" $remoteCommand) -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw "Remote Bible corpus audit query failed with exit code $LASTEXITCODE."
}
$snapshot = $raw | ConvertFrom-Json -Depth 20
$errors = [System.Collections.Generic.List[string]]::new()

function Assert-Equal {
    param([string]$Name, $Actual, $Expected)
    if ($Actual -ne $Expected) {
        $errors.Add("${Name}: expected=$Expected actual=$Actual")
    }
}

function Assert-Collection {
    param(
        [string]$CollectionId,
        [string]$Version,
        [string]$Visibility,
        [bool]$RedistributionAllowed,
        [string]$License,
        [long]$Chunks,
        [long]$Entities,
        [long]$Relations
    )
    $matches = @($snapshot.collections | Where-Object { $_.collectionId -eq $CollectionId -and $_.version -eq $Version })
    Assert-Equal "$CollectionId active collection count" $matches.Count 1
    if ($matches.Count -ne 1) { return }
    $item = $matches[0]
    Assert-Equal "$CollectionId visibility" $item.visibility $Visibility
    Assert-Equal "$CollectionId redistributionAllowed" $item.redistributionAllowed $RedistributionAllowed
    Assert-Equal "$CollectionId license" $item.license $License
    Assert-Equal "$CollectionId chunks" $item.chunks $Chunks
    Assert-Equal "$CollectionId entities" $item.entities $Entities
    Assert-Equal "$CollectionId relations" $item.relations $Relations
    Assert-Equal "$CollectionId aclGrants" $item.aclGrants 0
}

Assert-Collection 'bible-ko-nkrv' '0.1.0' 'private' $false 'copyrighted-restricted' 1693 0 31101
Assert-Collection 'bible-ko-tkv' '0.1.0' 'private' $false 'copyrighted-restricted' 2203 0 31097
Assert-Collection 'bible-original-step' '0.1.0' 'public_shared' $true 'CC BY; CC BY 4.0; CC BY-SA 4.0' 48515 4259 456058
Assert-Collection 'bible-reviewed-relations' '0.2.0' 'public_shared' $true 'CC BY 4.0 review metadata; underlying source references retain their licenses' 9 0 38
Assert-Equal 'public original candidate count' $snapshot.publicOriginalCandidateCount 0
Assert-Equal 'public original non-public source count' $snapshot.publicOriginalNonPublicSourceCount 0
Assert-Equal 'Paul entity with Saul and G4569G aliases' $snapshot.paulEntityCount 1
Assert-Equal 'King Saul entity' $snapshot.kingSaulEntityCount 1
Assert-Equal 'Acts 13:9 Paul mention' $snapshot.paulActsMentionCount 1
Assert-Equal 'forbidden King Saul-Paul merge count' $snapshot.forbiddenKingSaulPaulMergeCount 0

$result = [ordered]@{
    schemaVersion = 1
    auditedAt = [DateTimeOffset]::UtcNow
    passed = $errors.Count -eq 0
    errors = @($errors)
    snapshot = $snapshot
}
$json = $result | ConvertTo-Json -Depth 20
if ($OutputPath) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
    [IO.File]::WriteAllText($resolvedOutput, $json, [Text.UTF8Encoding]::new($false))
}
$json
if ($errors.Count -ne 0) {
    exit 2
}
