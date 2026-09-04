using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public static class SlogsDbInitializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string CsharpPatternLogSlug = "modern-csharp-component-patterns";
    private const string CsharpPatternLogTitle = "C# 14 패턴으로 작업 판단 로그 남기기";
    private const string CsharpPatternLogSummary = "최신 C# 문법을 적용한 이유, 검증 흔적, 리비전 태그를 함께 남기는 작업 로그입니다.";
    private const string CsharpPatternLogBody = "# C# 작업 판단 로그\n\n초기화 구문, 패턴 매칭, 컬렉션 표기법을 적용할 때는 코드량만 줄이는 것이 아니라 선택 이유와 검증 결과를 함께 남겨야 다음 리비전에서 판단을 검색할 수 있습니다.";

    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await db.Database.EnsureCreatedAsync();
        await EnsureSchemaAsync(db);
        await SeedUsersAsync(db);
        await EnsureAdminAccountAsync(db);
        await EnsureUserProfileDefaultsAsync(db);
        await SeedPostsAsync(db);
        await EnsureSeedIdentityDefaultsAsync(db);
        await EnsurePostThumbnailDefaultsAsync(db);
        await EnsurePostRevisionBaselinesAsync(db);
        await EnsureLlmWikiSourceBaselinesAsync(db);
    }

    private static async Task EnsureSchemaAsync(SlogsDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SlogsMcpPolicyPrompt" (
                "Id" integer PRIMARY KEY,
                "Version" character varying(32) NOT NULL,
                "KoreanMarkdown" text NOT NULL,
                "EnglishMarkdown" text NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "UpdatedBy" character varying(80) NOT NULL
            );
            INSERT INTO "SlogsMcpPolicyPrompt" ("Id", "Version", "KoreanMarkdown", "EnglishMarkdown", "UpdatedAt", "UpdatedBy")
            VALUES (1, {0}, {1}, {2}, {3}, 'system')
            ON CONFLICT ("Id") DO UPDATE SET
                "Version" = EXCLUDED."Version",
                "KoreanMarkdown" = EXCLUDED."KoreanMarkdown",
                "EnglishMarkdown" = EXCLUDED."EnglishMarkdown",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            WHERE "SlogsMcpPolicyPrompt"."UpdatedBy" = 'system';
            """,
            SlogsMcpPolicyPrompt.Version,
            SlogsMcpPolicyPrompt.BuildKoreanMarkdown(),
            SlogsMcpPolicyPrompt.BuildEnglishMarkdown(),
            DateTimeOffset.UtcNow);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SkillRegistryVersions" (
                "Id" uuid PRIMARY KEY,
                "Slug" character varying(64) NOT NULL,
                "Version" character varying(32) NOT NULL,
                "VersionMajor" integer NOT NULL,
                "VersionMinor" integer NOT NULL,
                "VersionPatch" integer NOT NULL,
                "Description" character varying(500) NOT NULL,
                "ContentHash" character varying(64) NOT NULL,
                "PackageJson" jsonb NOT NULL,
                "CandidateEvidenceJson" jsonb NOT NULL,
                "ValidationReportJson" jsonb NOT NULL,
                "ValidationReportHash" character varying(64) NOT NULL,
                "EvaluationPayloadJson" jsonb NOT NULL,
                "ReviewEvidenceJson" jsonb NULL,
                "Status" character varying(32) NOT NULL,
                "SubmittedBy" character varying(80) NOT NULL,
                "ValidatedBy" character varying(80) NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "CK_SkillRegistryVersions_Status" CHECK ("Status" IN ('validated-candidate', 'validated')),
                CONSTRAINT "UX_SkillRegistryVersions_Slug_Version" UNIQUE ("Slug", "Version"),
                CONSTRAINT "UX_SkillRegistryVersions_ContentHash" UNIQUE ("ContentHash")
            );
            CREATE INDEX IF NOT EXISTS "IX_SkillRegistryVersions_Latest"
            ON "SkillRegistryVersions" ("Slug", "VersionMajor" DESC, "VersionMinor" DESC, "VersionPatch" DESC);

            CREATE TABLE IF NOT EXISTS "SkillRegistrySelections" (
                "Id" uuid PRIMARY KEY,
                "OwnerUserName" character varying(80) NOT NULL,
                "SkillSlug" character varying(64) NOT NULL,
                "ScopeKind" character varying(16) NOT NULL,
                "ProjectKey" character varying(300) NULL,
                "ProjectKeyKey" character varying(300) GENERATED ALWAYS AS (COALESCE("ProjectKey", '')) STORED,
                "ChoicePrompted" boolean NOT NULL,
                "AutoUpdate" boolean NOT NULL DEFAULT TRUE,
                "PinnedVersion" character varying(32) NULL,
                "DecisionEvidence" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "CK_SkillRegistrySelections_ScopeKind" CHECK ("ScopeKind" IN ('project', 'global', 'disabled')),
                CONSTRAINT "CK_SkillRegistrySelections_ProjectScope" CHECK ("ScopeKind" <> 'project' OR "ProjectKey" IS NOT NULL),
                CONSTRAINT "FK_SkillRegistrySelections_Users_OwnerUserName"
                    FOREIGN KEY ("OwnerUserName") REFERENCES "Users" ("UserName") ON DELETE CASCADE,
                CONSTRAINT "UX_SkillRegistrySelections_Scope"
                    UNIQUE ("OwnerUserName", "SkillSlug", "ScopeKind", "ProjectKeyKey")
            );
            CREATE INDEX IF NOT EXISTS "IX_SkillRegistrySelections_Resolve"
            ON "SkillRegistrySelections" ("OwnerUserName", "SkillSlug", "ProjectKeyKey");
            """);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"SkillRegistryVersions\" ADD COLUMN IF NOT EXISTS \"EvaluationPayloadJson\" jsonb NOT NULL DEFAULT '{{}}'::jsonb;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"SkillRegistryVersions\" ADD COLUMN IF NOT EXISTS \"ReviewEvidenceJson\" jsonb NULL;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Posts\" ADD COLUMN IF NOT EXISTS \"ThumbnailUrl\" character varying(500) NOT NULL DEFAULT '';");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Posts\" ADD COLUMN IF NOT EXISTS \"IsDraft\" boolean NOT NULL DEFAULT FALSE;");
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PostRevisions" (
                "Id" uuid NOT NULL,
                "PostId" uuid NOT NULL,
                "RevisionNumber" integer NOT NULL,
                "Title" character varying(200) NOT NULL,
                "Summary" character varying(500) NOT NULL,
                "ThumbnailUrl" character varying(500) NOT NULL DEFAULT '',
                "Body" text NOT NULL,
                "TagsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
                "SeriesJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
                "CreatedAt" timestamp with time zone NOT NULL,
                "Author" character varying(80) NOT NULL,
                CONSTRAINT "PK_PostRevisions" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_PostRevisions_Posts_PostId"
                    FOREIGN KEY ("PostId") REFERENCES "Posts" ("Id") ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_PostRevisions_PostId"
            ON "PostRevisions" ("PostId");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PostRevisions_PostId_RevisionNumber"
            ON "PostRevisions" ("PostId", "RevisionNumber");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PostImages" (
                "Id" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "PostId" uuid NULL,
                "Url" character varying(500) NOT NULL,
                "FileName" character varying(260) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "LastReferencedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_PostImages" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_PostImages_Users_OwnerUserName"
                    FOREIGN KEY ("OwnerUserName") REFERENCES "Users" ("UserName") ON DELETE CASCADE,
                CONSTRAINT "FK_PostImages_Posts_PostId"
                    FOREIGN KEY ("PostId") REFERENCES "Posts" ("Id") ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM "PostImages" AS pi
            WHERE NOT EXISTS (
                SELECT 1
                FROM "Users" AS u
                WHERE u."UserName" = pi."OwnerUserName"
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = 'FK_PostImages_Users_OwnerUserName'
                ) THEN
                    ALTER TABLE "PostImages"
                    ADD CONSTRAINT "FK_PostImages_Users_OwnerUserName"
                    FOREIGN KEY ("OwnerUserName") REFERENCES "Users" ("UserName") ON DELETE CASCADE;
                END IF;
            END $$;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_PostImages_OwnerUserName_PostId"
            ON "PostImages" ("OwnerUserName", "PostId");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_PostImages_PostId"
            ON "PostImages" ("PostId");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_PostImages_Url"
            ON "PostImages" ("Url");
            """);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"ProfileImageUrl\" character varying(500) NOT NULL DEFAULT '';");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"Bio\" character varying(280) NOT NULL DEFAULT '';");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"Email\" character varying(320) NOT NULL DEFAULT '';");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"ProfileUpdatedAt\" timestamp with time zone NULL;");
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ExternalLogins" (
                "Provider" character varying(40) NOT NULL,
                "ProviderUserId" character varying(200) NOT NULL,
                "UserName" character varying(80) NOT NULL,
                "Email" character varying(320) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "LastLoginAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_ExternalLogins" PRIMARY KEY ("Provider", "ProviderUserId")
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ExternalLogins_UserName"
            ON "ExternalLogins" ("UserName");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ExternalLogins_Provider_Email"
            ON "ExternalLogins" ("Provider", "Email");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Users" AS u
            SET "Email" = latest."Email"
            FROM (
                SELECT DISTINCT ON ("UserName") "UserName", "Email"
                FROM "ExternalLogins"
                WHERE COALESCE("Email", '') <> ''
                ORDER BY "UserName", "LastLoginAt" DESC
            ) AS latest
            WHERE u."UserName" = latest."UserName"
                AND u."ProfileUpdatedAt" IS NULL
                AND COALESCE(u."Email", '') = '';
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "LlmWikiEntries" (
                "Id" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Slug" character varying(160) NOT NULL,
                "Title" character varying(200) NOT NULL,
                "Summary" character varying(500) NOT NULL,
                "Content" text NOT NULL,
                "SourcePrompt" text NOT NULL,
                "TagsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
                "CategoryPath" character varying(240) NOT NULL DEFAULT 'general',
                "CategoryDepth" integer NOT NULL DEFAULT 1,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "LastAccessedAt" timestamp with time zone NULL,
                "AccessCount" integer NOT NULL DEFAULT 0,
                "IsPublic" boolean NOT NULL DEFAULT FALSE,
                "PublishedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_LlmWikiEntries" PRIMARY KEY ("Id")
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "LlmWikiMcpTokens" (
                "Id" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Name" character varying(120) NOT NULL,
                "TokenHash" character varying(128) NOT NULL,
                "TokenPrefix" character varying(32) NOT NULL,
                "ScopesJson" jsonb NOT NULL DEFAULT '["mcp"]'::jsonb,
                "CreatedAt" timestamp with time zone NOT NULL,
                "LastUsedAt" timestamp with time zone NULL,
                "RevokedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_LlmWikiMcpTokens" PRIMARY KEY ("Id")
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "LlmWikiMcpTokens"
            ADD COLUMN IF NOT EXISTS "ScopesJson" jsonb NOT NULL DEFAULT '["mcp"]'::jsonb;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SlogsSettings" (
                "Key" character varying(120) NOT NULL,
                "Value" text NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SlogsSettings" PRIMARY KEY ("Key")
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ObsidianVaults" (
                "Id" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Name" character varying(120) NOT NULL,
                "NameKey" character varying(120) NOT NULL,
                "CurrentVersion" bigint NOT NULL DEFAULT 0,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_ObsidianVaults" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_ObsidianVaults_Users_OwnerUserName"
                    FOREIGN KEY ("OwnerUserName") REFERENCES "Users" ("UserName") ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ObsidianVaultFiles" (
                "Id" uuid NOT NULL,
                "VaultId" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Path" character varying(700) NOT NULL,
                "PathKey" character varying(700) NOT NULL,
                "Content" text NOT NULL,
                "ContentHash" character varying(64) NOT NULL,
                "MediaType" character varying(120) NOT NULL DEFAULT 'text/markdown',
                "Scope" character varying(40) NOT NULL DEFAULT 'markdown',
                "Kind" character varying(40) NOT NULL DEFAULT 'markdown',
                "Encoding" character varying(20) NOT NULL DEFAULT 'utf8',
                "MetadataJson" jsonb NOT NULL DEFAULT '{{}}'::jsonb,
                "LastClientId" character varying(120) NOT NULL DEFAULT '',
                "SizeBytes" bigint NOT NULL DEFAULT 0,
                "Version" bigint NOT NULL,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "DeletedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_ObsidianVaultFiles" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_ObsidianVaultFiles_ObsidianVaults_VaultId"
                    FOREIGN KEY ("VaultId") REFERENCES "ObsidianVaults" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ObsidianVaultFiles_Users_OwnerUserName"
                    FOREIGN KEY ("OwnerUserName") REFERENCES "Users" ("UserName") ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "ObsidianVaultFiles"
            ADD COLUMN IF NOT EXISTS "Scope" character varying(40) NOT NULL DEFAULT 'markdown';
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "ObsidianVaultFiles"
            ADD COLUMN IF NOT EXISTS "Kind" character varying(40) NOT NULL DEFAULT 'markdown';
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "ObsidianVaultFiles"
            ADD COLUMN IF NOT EXISTS "Encoding" character varying(20) NOT NULL DEFAULT 'utf8';
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "ObsidianVaultFiles"
            ADD COLUMN IF NOT EXISTS "MetadataJson" jsonb NOT NULL DEFAULT '{{}}'::jsonb;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "ObsidianVaultFiles"
            ADD COLUMN IF NOT EXISTS "LastClientId" character varying(120) NOT NULL DEFAULT '';
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ObsidianVaultClients" (
                "VaultId" uuid NOT NULL,
                "ClientId" character varying(120) NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "ClientName" character varying(120) NOT NULL,
                "ClientKind" character varying(80) NOT NULL,
                "LastSeenVersion" bigint NOT NULL DEFAULT 0,
                "CreatedAt" timestamp with time zone NOT NULL,
                "LastSeenAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_ObsidianVaultClients" PRIMARY KEY ("VaultId", "ClientId"),
                CONSTRAINT "FK_ObsidianVaultClients_ObsidianVaults_VaultId"
                    FOREIGN KEY ("VaultId") REFERENCES "ObsidianVaults" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ObsidianVaultClients_Users_OwnerUserName"
                    FOREIGN KEY ("OwnerUserName") REFERENCES "Users" ("UserName") ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ObsidianVaultFileVersions" (
                "Id" uuid NOT NULL,
                "FileId" uuid NOT NULL,
                "VaultId" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Path" character varying(700) NOT NULL,
                "PathKey" character varying(700) NOT NULL,
                "ContentHash" character varying(64) NOT NULL,
                "MediaType" character varying(120) NOT NULL DEFAULT 'text/markdown',
                "Scope" character varying(40) NOT NULL DEFAULT 'markdown',
                "Kind" character varying(40) NOT NULL DEFAULT 'markdown',
                "Encoding" character varying(20) NOT NULL DEFAULT 'utf8',
                "MetadataJson" jsonb NOT NULL DEFAULT '{{}}'::jsonb,
                "SizeBytes" bigint NOT NULL DEFAULT 0,
                "Version" bigint NOT NULL,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "DeletedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_ObsidianVaultFileVersions" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_ObsidianVaultFileVersions_Users_OwnerUserName"
                    FOREIGN KEY ("OwnerUserName") REFERENCES "Users" ("UserName") ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "LlmWikiEntrySources" (
                "Id" uuid NOT NULL,
                "EntryId" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Action" character varying(40) NOT NULL,
                "Prompt" text NOT NULL,
                "Content" text NULL,
                "Title" character varying(200) NULL,
                "Tags" text NULL,
                "CategoryPath" character varying(240) NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiEntrySources" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_LlmWikiEntrySources_LlmWikiEntries_EntryId"
                    FOREIGN KEY ("EntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "LlmWikiMcpAudits" (
                "Id" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "ToolName" character varying(80) NOT NULL,
                "ResponseMode" character varying(80) NOT NULL,
                "QueryHash" character varying(64) NOT NULL DEFAULT '',
                "QueryPreview" character varying(240) NOT NULL DEFAULT '',
                "CategoryPath" character varying(240) NOT NULL DEFAULT '',
                "RequestedLimit" integer NULL,
                "EffectiveLimit" integer NULL,
                "MinRelevancePercent" integer NULL,
                "ResultCount" integer NOT NULL DEFAULT 0,
                "ResultIdsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
                "ElapsedMs" integer NOT NULL DEFAULT 0,
                "ResponseChars" integer NOT NULL DEFAULT 0,
                "Succeeded" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiMcpAudits" PRIMARY KEY ("Id")
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "LlmWikiEntries"
            ADD COLUMN IF NOT EXISTS "SearchVector" tsvector
            GENERATED ALWAYS AS (
                setweight(to_tsvector('simple', coalesce("Title", '')), 'A') ||
                setweight(to_tsvector('simple', coalesce("Summary", '')), 'B') ||
                setweight(to_tsvector('simple', coalesce("SourcePrompt", '')), 'B') ||
                setweight(to_tsvector('simple', coalesce("Content", '')), 'C')
            ) STORED;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "LlmWikiEntries"
            ADD COLUMN IF NOT EXISTS "CategoryPath" character varying(240) NOT NULL DEFAULT 'general';
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "LlmWikiEntries"
            ADD COLUMN IF NOT EXISTS "CategoryDepth" integer NOT NULL DEFAULT 1;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "LlmWikiEntries"
            ADD COLUMN IF NOT EXISTS "IsPublic" boolean NOT NULL DEFAULT FALSE;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "LlmWikiEntries"
            ADD COLUMN IF NOT EXISTS "PublishedAt" timestamp with time zone NULL;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "LlmWikiEntries"
            SET "CategoryPath" = COALESCE(
                    (
                        SELECT string_agg(token, '/' ORDER BY ord)
                        FROM (
                            SELECT
                                ord,
                                NULLIF(
                                    trim(BOTH '-' FROM regexp_replace(lower(trim(value)), '[^0-9a-z가-힣_-]+', '-', 'g')),
                                    ''
                                ) AS token
                            FROM jsonb_array_elements_text("TagsJson"::jsonb) WITH ORDINALITY AS tags(value, ord)
                            ORDER BY ord
                            LIMIT 3
                        ) AS tag_tokens
                        WHERE token IS NOT NULL
                    ),
                    'general'
                )
            WHERE "CategoryPath" = 'general'
              AND jsonb_typeof("TagsJson"::jsonb) = 'array'
              AND jsonb_array_length("TagsJson"::jsonb) > 0;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "LlmWikiEntries"
            SET "CategoryDepth" = cardinality(string_to_array(NULLIF("CategoryPath", ''), '/'))
            WHERE "CategoryDepth" <= 1
              AND "CategoryPath" <> '';
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "LlmWikiEntryEmbeddings" (
                "EntryId" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Model" character varying(80) NOT NULL,
                "Dimensions" integer NOT NULL,
                "ContentHash" character varying(64) NOT NULL,
                "IndexVersion" character varying(40) NOT NULL DEFAULT '',
                "Embedding" vector(1024) NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiEntryEmbeddings" PRIMARY KEY ("EntryId"),
                CONSTRAINT "FK_LlmWikiEntryEmbeddings_LlmWikiEntries_EntryId"
                    FOREIGN KEY ("EntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "LlmWikiEntryEmbeddings"
            ADD COLUMN IF NOT EXISTS "IndexVersion" character varying(40) NOT NULL DEFAULT '';
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "LlmWikiEntryGraphNodes" (
                "EntryId" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "NodeKey" character varying(180) NOT NULL,
                "NodeText" character varying(120) NOT NULL,
                "NodeType" character varying(40) NOT NULL,
                "Weight" double precision NOT NULL,
                CONSTRAINT "PK_LlmWikiEntryGraphNodes" PRIMARY KEY ("EntryId", "NodeKey"),
                CONSTRAINT "FK_LlmWikiEntryGraphNodes_LlmWikiEntries_EntryId"
                    FOREIGN KEY ("EntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
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
                CONSTRAINT "FK_LlmWikiGraphEdges_FromEntry"
                    FOREIGN KEY ("FromEntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_LlmWikiGraphEdges_ToEntry"
                    FOREIGN KEY ("ToEntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiEntrySemanticRelations" (
                "Id" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "AnchorEntryId" uuid NOT NULL,
                "RelatedEntryId" uuid NOT NULL,
                "RelationType" character varying(40) NOT NULL,
                "Direction" character varying(16) NOT NULL,
                "Confidence" double precision NOT NULL,
                "State" character varying(24) NOT NULL,
                "AnchorEvidenceQuote" text NOT NULL,
                "RelatedEvidenceQuote" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "LastValidatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiEntrySemanticRelations" PRIMARY KEY ("Id"),
                CONSTRAINT "UQ_LlmWikiEntrySemanticRelations_TypedEdge"
                    UNIQUE ("OwnerUserName", "AnchorEntryId", "RelatedEntryId", "RelationType", "Direction"),
                CONSTRAINT "FK_LlmWikiEntrySemanticRelations_Anchor"
                    FOREIGN KEY ("AnchorEntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_LlmWikiEntrySemanticRelations_Related"
                    FOREIGN KEY ("RelatedEntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE,
                CONSTRAINT "CK_LlmWikiEntrySemanticRelations_Confidence" CHECK ("Confidence" >= 0.0 AND "Confidence" <= 1.0),
                CONSTRAINT "CK_LlmWikiEntrySemanticRelations_State" CHECK ("State" IN ('active', 'retired', 'rejected')),
                CONSTRAINT "CK_LlmWikiEntrySemanticRelations_Direction" CHECK ("Direction" IN ('outgoing', 'incoming')),
                CONSTRAINT "CK_LlmWikiEntrySemanticRelations_Endpoints" CHECK ("AnchorEntryId" <> "RelatedEntryId")
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiSemanticGraphVersions" (
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "SchemaVersion" integer NOT NULL,
                "CorpusSha256" character varying(64) NOT NULL,
                "Generator" character varying(120) NOT NULL,
                "GeneratorVersion" character varying(120) NOT NULL,
                "State" character varying(24) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ActivatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_LlmWikiSemanticGraphVersions"
                    PRIMARY KEY ("OwnerUserName", "Version"),
                CONSTRAINT "CK_LlmWikiSemanticGraphVersions_State"
                    CHECK ("State" IN ('candidate', 'validated', 'active', 'retired'))
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiSemanticEntities" (
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "EntityKey" character varying(180) NOT NULL,
                "CanonicalName" character varying(240) NOT NULL,
                "EntityType" character varying(40) NOT NULL,
                "Description" text NOT NULL,
                CONSTRAINT "PK_LlmWikiSemanticEntities"
                    PRIMARY KEY ("OwnerUserName", "Version", "EntityKey"),
                CONSTRAINT "FK_LlmWikiSemanticEntities_Version"
                    FOREIGN KEY ("OwnerUserName", "Version")
                    REFERENCES "LlmWikiSemanticGraphVersions" ("OwnerUserName", "Version") ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiSemanticMentions" (
                "Id" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "EntityKey" character varying(180) NOT NULL,
                "EntryId" uuid NOT NULL,
                "SourceId" uuid NULL,
                "EvidenceField" character varying(24) NOT NULL,
                "EvidenceQuote" text NOT NULL,
                "Confidence" double precision NOT NULL,
                CONSTRAINT "PK_LlmWikiSemanticMentions" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_LlmWikiSemanticMentions_Entity"
                    FOREIGN KEY ("OwnerUserName", "Version", "EntityKey")
                    REFERENCES "LlmWikiSemanticEntities" ("OwnerUserName", "Version", "EntityKey") ON DELETE CASCADE,
                CONSTRAINT "FK_LlmWikiSemanticMentions_Entry"
                    FOREIGN KEY ("EntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_LlmWikiSemanticMentions_Source"
                    FOREIGN KEY ("SourceId") REFERENCES "LlmWikiEntrySources" ("Id") ON DELETE CASCADE,
                CONSTRAINT "CK_LlmWikiSemanticMentions_Confidence" CHECK ("Confidence" >= 0.0 AND "Confidence" <= 1.0)
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiSemanticRelations" (
                "Id" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "FromEntityKey" character varying(180) NOT NULL,
                "ToEntityKey" character varying(180) NOT NULL,
                "RelationType" character varying(40) NOT NULL,
                "Confidence" double precision NOT NULL,
                "State" character varying(24) NOT NULL,
                CONSTRAINT "PK_LlmWikiSemanticRelations" PRIMARY KEY ("Id"),
                CONSTRAINT "UQ_LlmWikiSemanticRelations_TypedEdge"
                    UNIQUE ("OwnerUserName", "Version", "FromEntityKey", "RelationType", "ToEntityKey"),
                CONSTRAINT "FK_LlmWikiSemanticRelations_FromEntity"
                    FOREIGN KEY ("OwnerUserName", "Version", "FromEntityKey")
                    REFERENCES "LlmWikiSemanticEntities" ("OwnerUserName", "Version", "EntityKey") ON DELETE CASCADE,
                CONSTRAINT "FK_LlmWikiSemanticRelations_ToEntity"
                    FOREIGN KEY ("OwnerUserName", "Version", "ToEntityKey")
                    REFERENCES "LlmWikiSemanticEntities" ("OwnerUserName", "Version", "EntityKey") ON DELETE CASCADE,
                CONSTRAINT "CK_LlmWikiSemanticRelations_Confidence" CHECK ("Confidence" >= 0.0 AND "Confidence" <= 1.0),
                CONSTRAINT "CK_LlmWikiSemanticRelations_State" CHECK ("State" IN ('candidate', 'validated', 'active', 'rejected')),
                CONSTRAINT "CK_LlmWikiSemanticRelations_Endpoints" CHECK ("FromEntityKey" <> "ToEntityKey")
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiSemanticRelationEvidence" (
                "Id" uuid NOT NULL,
                "RelationId" uuid NOT NULL,
                "EntryId" uuid NOT NULL,
                "SourceId" uuid NULL,
                "EvidenceField" character varying(24) NOT NULL,
                "EvidenceQuote" text NOT NULL,
                CONSTRAINT "PK_LlmWikiSemanticRelationEvidence" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_LlmWikiSemanticRelationEvidence_Relation"
                    FOREIGN KEY ("RelationId") REFERENCES "LlmWikiSemanticRelations" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_LlmWikiSemanticRelationEvidence_Entry"
                    FOREIGN KEY ("EntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_LlmWikiSemanticRelationEvidence_Source"
                    FOREIGN KEY ("SourceId") REFERENCES "LlmWikiEntrySources" ("Id") ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiMemorySplitProposals" (
                "Id" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "SourceEntryId" uuid NOT NULL,
                "CreatedEntryId" uuid NULL,
                "ProposedTitle" character varying(240) NOT NULL,
                "ProposedCategoryPath" character varying(320) NOT NULL,
                "ProposedPrompt" text NOT NULL,
                "ProposedContent" text NOT NULL,
                "Reason" text NOT NULL,
                "EvidenceJson" jsonb NOT NULL,
                "State" character varying(24) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ActivatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_LlmWikiMemorySplitProposals" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_LlmWikiMemorySplitProposals_Version"
                    FOREIGN KEY ("OwnerUserName", "Version")
                    REFERENCES "LlmWikiSemanticGraphVersions" ("OwnerUserName", "Version") ON DELETE CASCADE,
                CONSTRAINT "FK_LlmWikiMemorySplitProposals_SourceEntry"
                    FOREIGN KEY ("SourceEntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_LlmWikiMemorySplitProposals_CreatedEntry"
                    FOREIGN KEY ("CreatedEntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE SET NULL,
                CONSTRAINT "CK_LlmWikiMemorySplitProposals_State"
                    CHECK ("State" IN ('candidate', 'validated', 'active', 'rejected', 'rolled-back'))
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            $"""
            TRUNCATE TABLE "LlmWikiGraphEdges", "LlmWikiGraphNodeStatistics", "LlmWikiGraphIndexStates";
            INSERT INTO "LlmWikiGraphNodeStatistics"
                ("OwnerUserName", "NodeKey", "EntryCount", "IndexVersion", "UpdatedAt")
            SELECT
                "OwnerUserName",
                "NodeKey",
                COUNT(DISTINCT "EntryId")::integer,
                '{LlmWikiGraphSearchCommand.GraphIndexVersion}',
                NOW()
            FROM "LlmWikiEntryGraphNodes"
            GROUP BY "OwnerUserName", "NodeKey";
            WITH scored_edges AS (
                SELECT
                    source_nodes."OwnerUserName",
                    source_nodes."EntryId" AS "FromEntryId",
                    neighbor_nodes."EntryId" AS "ToEntryId",
                    LEAST(
                        SUM(
                            LEAST(source_nodes."Weight", neighbor_nodes."Weight")
                            / LN(2.0 + frequency."EntryCount")
                        ),
                        1.0
                    ) AS "EdgeScore"
                FROM "LlmWikiEntryGraphNodes" AS source_nodes
                INNER JOIN "LlmWikiEntryGraphNodes" AS neighbor_nodes
                    ON neighbor_nodes."OwnerUserName" = source_nodes."OwnerUserName"
                   AND neighbor_nodes."NodeKey" = source_nodes."NodeKey"
                   AND neighbor_nodes."EntryId" <> source_nodes."EntryId"
                INNER JOIN "LlmWikiGraphNodeStatistics" AS frequency
                    ON frequency."OwnerUserName" = source_nodes."OwnerUserName"
                   AND frequency."NodeKey" = source_nodes."NodeKey"
                GROUP BY source_nodes."OwnerUserName", source_nodes."EntryId", neighbor_nodes."EntryId"
            ),
            ranked_edges AS (
                SELECT *, ROW_NUMBER() OVER (
                    PARTITION BY "OwnerUserName", "FromEntryId"
                    ORDER BY "EdgeScore" DESC, "ToEntryId"
                ) AS edge_rank
                FROM scored_edges
            )
            INSERT INTO "LlmWikiGraphEdges"
                ("OwnerUserName", "FromEntryId", "ToEntryId", "EdgeScore", "IndexVersion", "UpdatedAt")
            SELECT
                "OwnerUserName", "FromEntryId", "ToEntryId", "EdgeScore",
                '{LlmWikiGraphSearchCommand.GraphIndexVersion}', NOW()
            FROM ranked_edges
            WHERE edge_rank <= 4;
            INSERT INTO "LlmWikiGraphIndexStates"
                ("OwnerUserName", "IndexVersion", "SourceNodeCount", "BuiltAt")
            SELECT
                "OwnerUserName",
                '{LlmWikiGraphSearchCommand.GraphIndexVersion}',
                COUNT(*)::bigint,
                NOW()
            FROM "LlmWikiEntryGraphNodes"
            GROUP BY "OwnerUserName";
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_LlmWikiEntries_OwnerUserName_Slug"
            ON "LlmWikiEntries" ("OwnerUserName", "Slug");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntries_OwnerUserName_UpdatedAt"
            ON "LlmWikiEntries" ("OwnerUserName", "UpdatedAt" DESC);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntries_OwnerUserName_CategoryPath_UpdatedAt"
            ON "LlmWikiEntries" ("OwnerUserName", "CategoryPath", "UpdatedAt" DESC);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntries_PublicOwner_UpdatedAt"
            ON "LlmWikiEntries" ("OwnerUserName", "UpdatedAt" DESC)
            WHERE "IsPublic" = TRUE;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntries_PublicOwner_CategoryPath_UpdatedAt"
            ON "LlmWikiEntries" ("OwnerUserName", "CategoryPath", "UpdatedAt" DESC)
            WHERE "IsPublic" = TRUE;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntries_SearchVector"
            ON "LlmWikiEntries" USING GIN ("SearchVector");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntrySources_EntryId_CreatedAt"
            ON "LlmWikiEntrySources" ("EntryId", "CreatedAt");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntrySources_OwnerUserName_CreatedAt"
            ON "LlmWikiEntrySources" ("OwnerUserName", "CreatedAt");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiMcpAudits_OwnerUserName_CreatedAt"
            ON "LlmWikiMcpAudits" ("OwnerUserName", "CreatedAt" DESC);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiMcpAudits_CreatedAt"
            ON "LlmWikiMcpAudits" ("CreatedAt" DESC);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiMcpAudits_OwnerUserName_ToolName_CreatedAt"
            ON "LlmWikiMcpAudits" ("OwnerUserName", "ToolName", "CreatedAt" DESC);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiMcpAudits_ToolName_CreatedAt"
            ON "LlmWikiMcpAudits" ("ToolName", "CreatedAt" DESC);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiMcpAudits_ToolName_QueryHash_CreatedAt"
            ON "LlmWikiMcpAudits" ("ToolName", "QueryHash", "CreatedAt" DESC);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryEmbeddings_Owner_Model_Dimensions"
            ON "LlmWikiEntryEmbeddings" ("OwnerUserName", "Model", "Dimensions");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryEmbeddings_Owner_Model_Dimensions_IndexVersion"
            ON "LlmWikiEntryEmbeddings" ("OwnerUserName", "Model", "Dimensions", "IndexVersion");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryEmbeddings_Embedding_Hnsw"
            ON "LlmWikiEntryEmbeddings"
            USING hnsw ("Embedding" vector_cosine_ops);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryGraphNodes_Owner_NodeKey"
            ON "LlmWikiEntryGraphNodes" ("OwnerUserName", "NodeKey");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryGraphNodes_EntryId"
            ON "LlmWikiEntryGraphNodes" ("EntryId");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryGraphNodes_Owner_NodeKey_EntryId_Covering"
            ON "LlmWikiEntryGraphNodes" ("OwnerUserName", "NodeKey", "EntryId")
            INCLUDE ("Weight");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryGraphNodes_EntryId_Owner_NodeKey_Covering"
            ON "LlmWikiEntryGraphNodes" ("EntryId", "OwnerUserName", "NodeKey")
            INCLUDE ("Weight");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiGraphEdges_Owner_From_Score_To"
            ON "LlmWikiGraphEdges" ("OwnerUserName", "FromEntryId", "EdgeScore" DESC, "ToEntryId");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiSemanticMentions_Owner_Version_Entry"
            ON "LlmWikiSemanticMentions" ("OwnerUserName", "Version", "EntryId", "EntityKey");
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiSemanticRelations_Owner_Version_From"
            ON "LlmWikiSemanticRelations" ("OwnerUserName", "Version", "FromEntityKey", "Confidence" DESC)
            WHERE "State" = 'active';
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiSemanticRelations_Owner_Version_To"
            ON "LlmWikiSemanticRelations" ("OwnerUserName", "Version", "ToEntityKey", "Confidence" DESC)
            WHERE "State" = 'active';
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiMemorySplitProposals_Owner_Source_State"
            ON "LlmWikiMemorySplitProposals" ("OwnerUserName", "SourceEntryId", "State");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "LlmWikiKnowledgeCollections" (
                "CollectionId" character varying(120) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "Title" character varying(200) NOT NULL,
                "Domain" character varying(80) NOT NULL,
                "Language" character varying(40) NOT NULL,
                "License" character varying(120) NOT NULL,
                "SourceUri" character varying(1000) NOT NULL,
                "OwnerKind" character varying(24) NOT NULL DEFAULT 'user',
                "OwnerKey" character varying(160) NOT NULL DEFAULT '',
                "Visibility" character varying(40) NOT NULL,
                "ScopeKey" character varying(160) NULL,
                "RedistributionAllowed" boolean NOT NULL,
                "ExpectedChunkCount" integer NOT NULL,
                "Status" character varying(24) NOT NULL,
                "ContentHash" character varying(64) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "ActivatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_LlmWikiKnowledgeCollections" PRIMARY KEY ("CollectionId", "Version", "OwnerUserName"),
                CONSTRAINT "CK_LlmWikiKnowledgeCollections_OwnerKind" CHECK ("OwnerKind" IN ('user', 'organization', 'system')),
                CONSTRAINT "CK_LlmWikiKnowledgeCollections_Visibility" CHECK ("Visibility" IN ('private', 'organization', 'public_shared')),
                CONSTRAINT "CK_LlmWikiKnowledgeCollections_OrganizationScope" CHECK ("Visibility" <> 'organization' OR "ScopeKey" IS NOT NULL),
                CONSTRAINT "CK_LlmWikiKnowledgeCollections_Status" CHECK ("Status" IN ('staging', 'active', 'retired')),
                CONSTRAINT "CK_LlmWikiKnowledgeCollections_PublicLicense" CHECK ("Visibility" <> 'public_shared' OR "RedistributionAllowed" = TRUE)
            );

            ALTER TABLE "LlmWikiKnowledgeCollections"
                ADD COLUMN IF NOT EXISTS "OwnerKind" character varying(24) NOT NULL DEFAULT 'user',
                ADD COLUMN IF NOT EXISTS "OwnerKey" character varying(160) NOT NULL DEFAULT '';
            UPDATE "LlmWikiKnowledgeCollections"
            SET "OwnerKey" = "OwnerUserName"
            WHERE "OwnerKey" = '';
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_LlmWikiKnowledgeCollections_OwnerKind') THEN
                    ALTER TABLE "LlmWikiKnowledgeCollections"
                    ADD CONSTRAINT "CK_LlmWikiKnowledgeCollections_OwnerKind"
                    CHECK ("OwnerKind" IN ('user', 'organization', 'system'));
                END IF;
            END $$;

            CREATE TABLE IF NOT EXISTS "LlmWikiKnowledgeDocuments" (
                "CollectionId" character varying(120) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "DocumentId" character varying(180) NOT NULL,
                "Title" character varying(300) NOT NULL,
                "DocumentType" character varying(80) NOT NULL,
                "Ordinal" integer NOT NULL,
                "SourceLocator" character varying(1000) NOT NULL,
                "MetadataJson" jsonb NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiKnowledgeDocuments" PRIMARY KEY ("CollectionId", "Version", "OwnerUserName", "DocumentId"),
                CONSTRAINT "FK_LlmWikiKnowledgeDocuments_Collection" FOREIGN KEY ("CollectionId", "Version", "OwnerUserName")
                    REFERENCES "LlmWikiKnowledgeCollections" ("CollectionId", "Version", "OwnerUserName") ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiKnowledgeStructureNodes" (
                "CollectionId" character varying(120) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "NodeId" character varying(220) NOT NULL,
                "DocumentId" character varying(180) NOT NULL,
                "ParentNodeId" character varying(220) NULL,
                "NodeType" character varying(80) NOT NULL,
                "Label" character varying(300) NOT NULL,
                "Ordinal" integer NOT NULL,
                "Locator" character varying(500) NOT NULL,
                "MetadataJson" jsonb NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiKnowledgeStructureNodes" PRIMARY KEY ("CollectionId", "Version", "OwnerUserName", "NodeId"),
                CONSTRAINT "FK_LlmWikiKnowledgeStructureNodes_Document" FOREIGN KEY ("CollectionId", "Version", "OwnerUserName", "DocumentId")
                    REFERENCES "LlmWikiKnowledgeDocuments" ("CollectionId", "Version", "OwnerUserName", "DocumentId") ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiKnowledgeChunks" (
                "CollectionId" character varying(120) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "ChunkId" character varying(240) NOT NULL,
                "DocumentId" character varying(180) NOT NULL,
                "StructureNodeId" character varying(220) NULL,
                "Ordinal" integer NOT NULL,
                "Text" text NOT NULL,
                "StartLocator" character varying(500) NOT NULL,
                "EndLocator" character varying(500) NOT NULL,
                "PreviousChunkId" character varying(240) NULL,
                "NextChunkId" character varying(240) NULL,
                "OverlapUnits" integer NOT NULL,
                "TokenCount" integer NOT NULL,
                "TokenizerId" character varying(80) NOT NULL,
                "SearchAliasesJson" jsonb NOT NULL,
                "MetadataJson" jsonb NOT NULL,
                "SearchText" text NOT NULL,
                "ContentHash" character varying(64) NOT NULL,
                "EmbeddingModel" character varying(80) NOT NULL,
                "EmbeddingDimensions" integer NOT NULL,
                "IndexVersion" character varying(80) NOT NULL,
                "Embedding" vector(1024) NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiKnowledgeChunks" PRIMARY KEY ("CollectionId", "Version", "OwnerUserName", "ChunkId"),
                CONSTRAINT "FK_LlmWikiKnowledgeChunks_Document" FOREIGN KEY ("CollectionId", "Version", "OwnerUserName", "DocumentId")
                    REFERENCES "LlmWikiKnowledgeDocuments" ("CollectionId", "Version", "OwnerUserName", "DocumentId") ON DELETE CASCADE,
                CONSTRAINT "CK_LlmWikiKnowledgeChunks_TokenCount" CHECK ("TokenCount" > 0),
                CONSTRAINT "CK_LlmWikiKnowledgeChunks_Overlap" CHECK ("OverlapUnits" >= 0)
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiEntryKnowledgeRelations" (
                "Id" uuid NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "AnchorEntryId" uuid NOT NULL,
                "TargetCollectionId" character varying(120) NOT NULL,
                "TargetVersion" character varying(80) NOT NULL,
                "TargetOwnerUserName" character varying(80) NOT NULL,
                "TargetChunkId" character varying(240) NOT NULL,
                "RelationType" character varying(40) NOT NULL,
                "Direction" character varying(16) NOT NULL,
                "Confidence" double precision NOT NULL,
                "State" character varying(24) NOT NULL,
                "AnchorEvidenceQuote" text NOT NULL,
                "TargetEvidenceQuote" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "LastValidatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiEntryKnowledgeRelations" PRIMARY KEY ("Id"),
                CONSTRAINT "UQ_LlmWikiEntryKnowledgeRelations_TypedEdge"
                    UNIQUE ("OwnerUserName", "AnchorEntryId", "TargetCollectionId", "TargetVersion", "TargetOwnerUserName", "TargetChunkId", "RelationType", "Direction"),
                CONSTRAINT "FK_LlmWikiEntryKnowledgeRelations_Anchor"
                    FOREIGN KEY ("AnchorEntryId") REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_LlmWikiEntryKnowledgeRelations_Target"
                    FOREIGN KEY ("TargetCollectionId", "TargetVersion", "TargetOwnerUserName", "TargetChunkId")
                    REFERENCES "LlmWikiKnowledgeChunks" ("CollectionId", "Version", "OwnerUserName", "ChunkId") ON DELETE CASCADE,
                CONSTRAINT "CK_LlmWikiEntryKnowledgeRelations_Confidence" CHECK ("Confidence" >= 0.0 AND "Confidence" <= 1.0),
                CONSTRAINT "CK_LlmWikiEntryKnowledgeRelations_State" CHECK ("State" IN ('active', 'retired', 'rejected')),
                CONSTRAINT "CK_LlmWikiEntryKnowledgeRelations_Direction" CHECK ("Direction" IN ('outgoing', 'incoming'))
            );

            ALTER TABLE "LlmWikiKnowledgeChunks"
            ADD COLUMN IF NOT EXISTS "SearchVector" tsvector
            GENERATED ALWAYS AS (to_tsvector('simple', coalesce("SearchText", ''))) STORED;

            CREATE TABLE IF NOT EXISTS "LlmWikiKnowledgeEntities" (
                "CollectionId" character varying(120) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "EntityId" character varying(240) NOT NULL,
                "EntityType" character varying(80) NOT NULL,
                "CanonicalLabel" character varying(300) NOT NULL,
                "AliasesJson" jsonb NOT NULL,
                "MetadataJson" jsonb NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiKnowledgeEntities" PRIMARY KEY ("CollectionId", "Version", "OwnerUserName", "EntityId")
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiKnowledgeRelations" (
                "CollectionId" character varying(120) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "RelationId" character varying(240) NOT NULL,
                "FromNodeId" character varying(240) NOT NULL,
                "RelationType" character varying(100) NOT NULL,
                "ToNodeId" character varying(240) NOT NULL,
                "ClaimClass" character varying(80) NOT NULL,
                "ReviewStatus" character varying(40) NOT NULL,
                "Confidence" double precision NOT NULL,
                "EvidenceJson" jsonb NOT NULL,
                "CreatedBy" character varying(80) NOT NULL,
                "MetadataJson" jsonb NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiKnowledgeRelations" PRIMARY KEY ("CollectionId", "Version", "OwnerUserName", "RelationId"),
                CONSTRAINT "CK_LlmWikiKnowledgeRelations_Confidence" CHECK ("Confidence" >= 0 AND "Confidence" <= 1),
                CONSTRAINT "CK_LlmWikiKnowledgeRelations_Status" CHECK ("ReviewStatus" IN ('candidate', 'approved', 'published', 'disputed', 'rejected'))
            );

            CREATE TABLE IF NOT EXISTS "LlmWikiKnowledgeCollectionAcl" (
                "CollectionId" character varying(120) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "OwnerUserName" character varying(80) NOT NULL,
                "PrincipalKind" character varying(24) NOT NULL,
                "PrincipalKey" character varying(160) NOT NULL,
                "Permission" character varying(24) NOT NULL,
                "GrantedByUserName" character varying(80) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LlmWikiKnowledgeCollectionAcl" PRIMARY KEY ("CollectionId", "Version", "OwnerUserName", "PrincipalKind", "PrincipalKey"),
                CONSTRAINT "FK_LlmWikiKnowledgeCollectionAcl_Collection" FOREIGN KEY ("CollectionId", "Version", "OwnerUserName")
                    REFERENCES "LlmWikiKnowledgeCollections" ("CollectionId", "Version", "OwnerUserName") ON DELETE CASCADE,
                CONSTRAINT "CK_LlmWikiKnowledgeCollectionAcl_PrincipalKind" CHECK ("PrincipalKind" IN ('user', 'organization')),
                CONSTRAINT "CK_LlmWikiKnowledgeCollectionAcl_Permission" CHECK ("Permission" IN ('reader', 'editor', 'maintainer'))
            );

            CREATE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeCollections_Visibility_Status"
            ON "LlmWikiKnowledgeCollections" ("Visibility", "Status", "Domain");
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeCollections_Owner"
            ON "LlmWikiKnowledgeCollections" ("OwnerKind", "OwnerKey", "Status");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeCollections_OwnerIdentity"
            ON "LlmWikiKnowledgeCollections" ("CollectionId", "Version", "OwnerKind", "OwnerKey");
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeCollectionAcl_Principal"
            ON "LlmWikiKnowledgeCollectionAcl" ("PrincipalKind", "PrincipalKey", "Permission");
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeDocuments_Order"
            ON "LlmWikiKnowledgeDocuments" ("CollectionId", "Version", "OwnerUserName", "Ordinal");
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeStructureNodes_Parent"
            ON "LlmWikiKnowledgeStructureNodes" ("CollectionId", "Version", "OwnerUserName", "ParentNodeId", "Ordinal");
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeChunks_Document_Order"
            ON "LlmWikiKnowledgeChunks" ("CollectionId", "Version", "OwnerUserName", "DocumentId", "Ordinal");
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeChunks_Embedding_Hnsw"
            ON "LlmWikiKnowledgeChunks" USING hnsw ("Embedding" vector_cosine_ops);
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeChunks_SearchVector_Gin"
            ON "LlmWikiKnowledgeChunks" USING gin ("SearchVector");
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntrySemanticRelations_Anchor_Active"
            ON "LlmWikiEntrySemanticRelations" ("OwnerUserName", "AnchorEntryId", "Confidence" DESC)
            WHERE "State"='active';
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntrySemanticRelations_Related_Active"
            ON "LlmWikiEntrySemanticRelations" ("OwnerUserName", "RelatedEntryId", "Confidence" DESC)
            WHERE "State"='active';
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryKnowledgeRelations_Anchor_Active"
            ON "LlmWikiEntryKnowledgeRelations" ("OwnerUserName", "AnchorEntryId", "Confidence" DESC)
            WHERE "State"='active';
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryKnowledgeRelations_Target_Active"
            ON "LlmWikiEntryKnowledgeRelations" ("TargetCollectionId", "TargetVersion", "TargetOwnerUserName", "TargetChunkId", "Confidence" DESC)
            WHERE "State"='active';
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeRelations_From"
            ON "LlmWikiKnowledgeRelations" ("CollectionId", "Version", "OwnerUserName", "FromNodeId", "ReviewStatus");
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiKnowledgeRelations_To"
            ON "LlmWikiKnowledgeRelations" ("CollectionId", "Version", "OwnerUserName", "ToNodeId", "ReviewStatus");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_LlmWikiMcpTokens_TokenHash"
            ON "LlmWikiMcpTokens" ("TokenHash");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiMcpTokens_OwnerUserName"
            ON "LlmWikiMcpTokens" ("OwnerUserName");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ObsidianVaults_OwnerUserName_NameKey"
            ON "ObsidianVaults" ("OwnerUserName", "NameKey");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ObsidianVaults_OwnerUserName_UpdatedAt"
            ON "ObsidianVaults" ("OwnerUserName", "UpdatedAt" DESC);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ObsidianVaultFiles_VaultId_PathKey"
            ON "ObsidianVaultFiles" ("VaultId", "PathKey");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ObsidianVaultFiles_OwnerUserName_VaultId_Version"
            ON "ObsidianVaultFiles" ("OwnerUserName", "VaultId", "Version");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ObsidianVaultFiles_OwnerUserName_VaultId_IsDeleted_UpdatedAt"
            ON "ObsidianVaultFiles" ("OwnerUserName", "VaultId", "IsDeleted", "UpdatedAt" DESC);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ObsidianVaultFiles_OwnerUserName_VaultId_Scope_Version"
            ON "ObsidianVaultFiles" ("OwnerUserName", "VaultId", "Scope", "Version");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ObsidianVaultClients_OwnerUserName_VaultId_LastSeenAt"
            ON "ObsidianVaultClients" ("OwnerUserName", "VaultId", "LastSeenAt" DESC);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ObsidianVaultFileVersions_OwnerUserName_VaultId_PathKey_Version"
            ON "ObsidianVaultFileVersions" ("OwnerUserName", "VaultId", "PathKey", "Version");
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ObsidianVaultFileVersions_OwnerUserName_VaultId_Version"
            ON "ObsidianVaultFileVersions" ("OwnerUserName", "VaultId", "Version");
            """);
    }

    private static async Task SeedUsersAsync(SlogsDbContext db)
    {
        if (await db.Users.AnyAsync())
        {
            return;
        }

        var users = new[]
        {
            ("admin", "관리자", string.Empty),
            ("guest", "손님", "guest"),
            ("devin", "devin", "devin"),
            ("junho", "junho", "junho"),
            ("mina", "mina", "mina"),
            ("alex", "alex", "alex"),
            ("jane", "jane", "jane"),
            ("kevin", "kevin", "kevin"),
            ("rose", "rose", "rose"),
            ("nate", "nate", "nate"),
            ("lee", "lee", "lee"),
            ("sora", "sora", "sora"),
            ("hyun", "hyun", "hyun")
        };

        foreach (var (userName, displayName, password) in users)
        {
            db.Users.Add(new UserRecord
            {
                UserName = userName,
                DisplayName = displayName,
                Email = string.Empty,
                Password = password,
                ProfileImageUrl = string.Empty,
                Bio = GetDefaultBio(userName),
                RegisteredAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureAdminAccountAsync(SlogsDbContext db)
    {
        var admin = await db.Users.FirstOrDefaultAsync(x => x.UserName == AuthUser.AdminUserName);
        if (admin is null)
        {
            db.Users.Add(new UserRecord
            {
                UserName = AuthUser.AdminUserName,
                DisplayName = "관리자",
                Email = string.Empty,
                Password = string.Empty,
                ProfileImageUrl = string.Empty,
                Bio = GetDefaultBio(AuthUser.AdminUserName),
                RegisteredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return;
        }

        var changed = false;
        if (admin.DisplayName is "운영 슬로거" or "")
        {
            admin.DisplayName = "관리자";
            changed = true;
        }

        if (string.IsNullOrEmpty(admin.Password))
        {
            if (changed)
            {
                await db.SaveChangesAsync();
            }

            return;
        }

        admin.Password = string.Empty;
        changed = true;
        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureUserProfileDefaultsAsync(SlogsDbContext db)
    {
        var users = await db.Users.ToListAsync();
        var changed = false;

        foreach (var user in users)
        {
            if (user.UserName.Equals(AuthUser.AdminUserName, StringComparison.OrdinalIgnoreCase)
                && user.DisplayName == "운영 슬로거"
                && user.ProfileUpdatedAt is null)
            {
                user.DisplayName = "관리자";
                changed = true;
            }

            if (IsLegacyDefaultProfileImageUrl(user.ProfileImageUrl))
            {
                user.ProfileImageUrl = string.Empty;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(user.Bio) && user.ProfileUpdatedAt is null)
            {
                user.Bio = GetDefaultBio(user.UserName);
                changed = true;
            }
            else if (user.ProfileUpdatedAt is null && IsLegacyDefaultBio(user.Bio))
            {
                user.Bio = GetDefaultBio(user.UserName);
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedPostsAsync(SlogsDbContext db)
    {
        if (await db.Posts.AnyAsync())
        {
            return;
        }

        var now = DateTime.UtcNow;
        var firstPost = new PostRecord
        {
            Title = "Blazor로 남기는 Markdown 지식 로그 구조",
            Author = "devin",
            Summary = "서버 렌더링과 인터랙티브 기능을 결합한 Blazor 앱에서 공개 로그, 태그, 슬로거 검색을 구성하는 방법입니다.",
            Body = "# Blazor 지식 로그 구조\n\n프로젝트를 시작하면 먼저 로그 구조를 잡고, 데이터 모델을 설계한 뒤 기억과 검증이 이어지는 화면별 컴포넌트를 배치하면 됩니다.",
            ThumbnailUrl = GetDefaultThumbnailUrl("blazor-markdown-knowledge-log"),
            PublishedAt = now.AddDays(-4),
            UpdatedAt = now,
            Slug = "blazor-markdown-knowledge-log",
            TagsJson = ToJson(["blazor", "dotnet", "csharp"]),
            SeriesJson = ToJson(["지식 로그 구조"]),
            LikedByJson = ToJson(["admin"]),
            ReadTimeMinutes = 6
        };

        firstPost.Comments.AddRange([
            CreateComment("guest", "좋은 로그네요. 라우팅 설계가 가장 먼저라고 동의합니다.", now.AddDays(-3).AddHours(-10)),
            CreateComment("mina", "샘플 데이터로 동작을 확인하기 좋은 예시입니다.", now.AddDays(-3).AddHours(-8)),
            CreateComment("junho", "컴포넌트 분리는 서비스 레이어 먼저 뽑는 게 맞아요.", now.AddDays(-3).AddHours(-6)),
            CreateComment("devin", "실시간 상호작용까지 고려하면 체감이 더 좋아집니다.", now.AddDays(-3).AddHours(-4)),
            CreateComment("alex", "로그 제목이 잘 보이도록 헤더 고정도 좋은 패턴 같아요.", now.AddDays(-3).AddHours(-2)),
            CreateComment("jane", "댓글이 많아지면 페이지네이션이 필요할 것 같아요.", now.AddDays(-2).AddHours(-10)),
            CreateComment("kevin", "태그 라우팅 동작은 실제 서비스에서 중요합니다.", now.AddDays(-2).AddHours(-8)),
            CreateComment("rose", "좋은 정렬 기준을 같이 고민하면 유저 피드백이 더 좋아져요.", now.AddDays(-2).AddHours(-6)),
            CreateComment("nate", "문서 정리 방식이 깔끔해서 이해가 빠르네요.", now.AddDays(-2).AddHours(-4)),
            CreateComment("lee", "댓글에 답글을 남길 수 있으면 대화를 이어가기 좋겠습니다.", now.AddDays(-2).AddHours(-2)),
            CreateComment("sora", "실전에서 캐시 전략만 보완하면 충분히 배포 가능한 수준입니다.", now.AddDays(-1).AddHours(-10)),
            CreateComment("hyun", "좋은 로그 감사합니다. 바로 따라 해보겠습니다.", now.AddDays(-1).AddHours(-8))
        ]);

        db.Posts.AddRange(
            firstPost,
            new PostRecord
            {
                Title = CsharpPatternLogTitle,
                Author = "junho",
                Summary = CsharpPatternLogSummary,
                Body = CsharpPatternLogBody,
                ThumbnailUrl = GetDefaultThumbnailUrl(CsharpPatternLogSlug),
                PublishedAt = now.AddDays(-2),
                UpdatedAt = now,
                Slug = CsharpPatternLogSlug,
                TagsJson = ToJson(["csharp", "programming", "architecture"]),
                SeriesJson = ToJson(["아키텍처 노트"]),
                LikedByJson = ToJson(["guest", "admin"]),
                ReadTimeMinutes = 9
            },
            new PostRecord
            {
                Title = "slogs 검색을 더 직관적으로 만들기",
                Author = "mina",
                Summary = "검색 입력, 사이드바 태그, 이어지는 로그 추천을 한 번에 정리한 slogs UX 설계 노트입니다.",
                Body = "# 검색 개선\n\n검색은 짧고 명확한 태그(슬로거, 제목, 태그)로 범위를 좁힐 수 있어야 사용자 편의성이 높습니다.",
                ThumbnailUrl = GetDefaultThumbnailUrl("recall-ux-in-slogs"),
                PublishedAt = now.AddDays(-1),
                UpdatedAt = now,
                Slug = "recall-ux-in-slogs",
                TagsJson = ToJson(["ux", "design", "recall"]),
                SeriesJson = ToJson(["검색 실험실"]),
                ReadTimeMinutes = 7,
                Comments =
                [
                    CreateComment("devin", "좋은 정리입니다. 태그 사용성을 강조한 구조가 좋네요.", now.AddHours(-5))
                ]
            });

        await db.SaveChangesAsync();
    }

    private static async Task EnsureSeedIdentityDefaultsAsync(SlogsDbContext db)
    {
        var changed = false;
        var legacySlug = "blazor-markdown-blog";
        var updatedSlug = "blazor-markdown-knowledge-log";
        var seedPost = await db.Posts
            .Include(x => x.Comments)
            .FirstOrDefaultAsync(x => x.Author == "devin"
                && (x.Slug == legacySlug || x.Slug == updatedSlug || x.Title == "Blazor로 만드는 Markdown 블로그 구조"));

        if (seedPost is not null)
        {
            seedPost.Title = "Blazor로 남기는 Markdown 지식 로그 구조";
            seedPost.Summary = "서버 렌더링과 인터랙티브 기능을 결합한 Blazor 앱에서 공개 로그, 태그, 슬로거 검색을 구성하는 방법입니다.";
            seedPost.Body = "# Blazor 지식 로그 구조\n\n프로젝트를 시작하면 먼저 로그 구조를 잡고, 데이터 모델을 설계한 뒤 기억과 검증이 이어지는 화면별 컴포넌트를 배치하면 됩니다.";
            if (seedPost.Slug == legacySlug
                && !await db.Posts.AnyAsync(x => x.Author == seedPost.Author && x.Slug == updatedSlug && x.Id != seedPost.Id))
            {
                seedPost.Slug = updatedSlug;
            }

            seedPost.ThumbnailUrl = GetDefaultThumbnailUrl(updatedSlug);
            seedPost.SeriesJson = ToJson(["지식 로그 구조"]);
            changed = true;

            foreach (var comment in seedPost.Comments)
            {
                comment.Content = comment.Content switch
                {
                    "좋은 포스트네요. 라우팅 설계가 가장 먼저라고 동의합니다." => "좋은 로그네요. 라우팅 설계가 가장 먼저라고 동의합니다.",
                    "글 제목이 잘 보이도록 헤더 고정도 좋은 패턴 같아요." => "로그 제목이 잘 보이도록 헤더 고정도 좋은 패턴 같아요.",
                    "대화 흔적 페이지네이션이 필요한 구간이 생길 것 같아요." => "댓글이 많아지면 페이지네이션이 필요할 것 같아요.",
                    "태그 라우팅 동작은 실제 서비스에서 중요합니다." => "태그 라우팅 동작은 실제 서비스에서 중요합니다.",
                    var content when IsLegacyReplyFeatureComment(content) => "댓글에 답글을 남길 수 있으면 대화를 이어가기 좋겠습니다.",
                    "좋은 글 감사합니다. 바로 따라 해보겠습니다." => "좋은 로그 감사합니다. 바로 따라 해보겠습니다.",
                    _ => comment.Content
                };
            }

            var revisions = await db.PostRevisions.Where(x => x.PostId == seedPost.Id).ToListAsync();
            foreach (var revision in revisions)
            {
                if (revision.Title == "Blazor로 만드는 Markdown 블로그 구조"
                    || revision.SeriesJson.Contains("블로그 시리즈", StringComparison.Ordinal))
                {
                    revision.Title = seedPost.Title;
                    revision.Summary = seedPost.Summary;
                    revision.Body = seedPost.Body;
                    revision.ThumbnailUrl = seedPost.ThumbnailUrl;
                    revision.SeriesJson = seedPost.SeriesJson;
                    changed = true;
                }
            }
        }

        var csharpPost = await db.Posts
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(x => x.Author == "junho" && x.Slug == CsharpPatternLogSlug);
        if (csharpPost is not null)
        {
            if (IsLegacyCsharpPatternLog(csharpPost))
            {
                ApplyCsharpPatternLogIdentity(csharpPost);
                changed = true;
            }

            foreach (var revision in csharpPost.Revisions)
            {
                if (IsLegacyCsharpPatternLog(revision))
                {
                    ApplyCsharpPatternLogIdentity(revision);
                    changed = true;
                }
            }
        }

        var legacySearchSlug = "ux-search-in-slogs";
        var recallUxSlug = "recall-ux-in-slogs";
        var searchPost = await db.Posts
            .Include(x => x.Comments)
            .Include(x => x.Revisions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Author == "mina"
                && (x.Slug == legacySearchSlug || x.Slug == recallUxSlug));
        if (searchPost is not null)
        {
            searchPost.Title = "slogs 검색을 더 직관적으로 만들기";
            searchPost.Summary = "검색 입력, 사이드바 태그, 이어지는 로그 추천을 한 번에 정리한 slogs UX 설계 노트입니다.";
            searchPost.Body = "# 검색 개선\n\n검색은 짧고 명확한 태그(슬로거, 제목, 태그)로 범위를 좁힐 수 있어야 사용자 편의성이 높습니다.";
            searchPost.TagsJson = ToJson(["ux", "design", "recall"]);
            searchPost.SeriesJson = ToJson(["검색 실험실"]);
            searchPost.ThumbnailUrl = GetDefaultThumbnailUrl(recallUxSlug);
            if (searchPost.Slug == legacySearchSlug
                && !await db.Posts.AnyAsync(x => x.Author == searchPost.Author && x.Slug == recallUxSlug && x.Id != searchPost.Id))
            {
                searchPost.Slug = recallUxSlug;
            }

            changed = true;

            foreach (var comment in searchPost.Comments)
            {
                if (comment.Content == "좋은 정리입니다. 태그 UX를 강조한 구조가 좋네요.")
                {
                    comment.Content = "좋은 정리입니다. 태그 사용성을 강조한 구조가 좋네요.";
                }
            }

            foreach (var revision in searchPost.Revisions)
            {
                if (revision.SeriesJson.Contains("UX 실험실", StringComparison.Ordinal)
                    || revision.TagsJson.Contains("search", StringComparison.Ordinal)
                    || revision.Title.Contains("검색 UX", StringComparison.Ordinal))
                {
                    revision.Title = searchPost.Title;
                    revision.Summary = searchPost.Summary;
                    revision.Body = searchPost.Body;
                    revision.ThumbnailUrl = searchPost.ThumbnailUrl;
                    revision.TagsJson = searchPost.TagsJson;
                    revision.SeriesJson = searchPost.SeriesJson;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsurePostThumbnailDefaultsAsync(SlogsDbContext db)
    {
        var posts = await db.Posts.ToListAsync();
        var changed = false;

        foreach (var post in posts)
        {
            if (!string.IsNullOrWhiteSpace(post.ThumbnailUrl))
            {
                continue;
            }

            post.ThumbnailUrl = GetDefaultThumbnailUrl(post.Slug);
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsurePostRevisionBaselinesAsync(SlogsDbContext db)
    {
        var posts = await db.Posts
            .Include(x => x.Revisions)
            .Where(x => !x.IsDraft)
            .ToListAsync();
        var changed = false;

        foreach (var post in posts)
        {
            if (post.Revisions.Count > 0)
            {
                continue;
            }

            db.PostRevisions.Add(new PostRevisionRecord
            {
                PostId = post.Id,
                RevisionNumber = 1,
                Title = post.Title,
                Summary = post.Summary,
                ThumbnailUrl = post.ThumbnailUrl,
                Body = post.Body,
                TagsJson = post.TagsJson,
                SeriesJson = post.SeriesJson,
                CreatedAt = post.PublishedAt,
                Author = post.Author
            });
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureLlmWikiSourceBaselinesAsync(SlogsDbContext db)
    {
        var entries = await db.LlmWikiEntries
            .Include(x => x.Sources)
            .Where(x => !x.Sources.Any())
            .ToListAsync();
        if (entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            db.LlmWikiEntrySources.Add(new LlmWikiEntrySourceRecord
            {
                Id = Guid.NewGuid(),
                EntryId = entry.Id,
                OwnerUserName = entry.OwnerUserName,
                Action = "legacy-baseline",
                Prompt = entry.SourcePrompt,
                Content = string.IsNullOrWhiteSpace(entry.Content) ? null : entry.Content,
                Title = entry.Title,
                Tags = FormatTags(entry.TagsJson),
                CategoryPath = entry.CategoryPath,
                CreatedAt = entry.CreatedAt
            });
        }

        await db.SaveChangesAsync();
    }

    private static string? FormatTags(string tagsJson)
    {
        var tags = JsonSerializer.Deserialize(tagsJson, GetJsonTypeInfo<string[]>()) ?? [];
        return tags.Length == 0 ? null : string.Join(", ", tags);
    }

    private static CommentRecord CreateComment(string author, string content, DateTime createdAt)
    {
        return new CommentRecord
        {
            Author = author,
            AuthorNormalized = NormalizeUser(author),
            Content = content,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private static string ToJson(IEnumerable<string> values)
    {
        return JsonSerializer.Serialize(values.ToArray(), GetJsonTypeInfo<string[]>());
    }

    private static JsonTypeInfo<T> GetJsonTypeInfo<T>()
    {
        return (JsonTypeInfo<T>?)SlogsJsonSerializerContext.Default.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"JSON metadata for {typeof(T).FullName} is not registered.");
    }

    private static string GetDefaultBio(string userName)
    {
        return NormalizeUser(userName) switch
        {
            "admin" => "slogs 운영 기준과 샘플 콘텐츠 품질을 점검합니다.",
            "devin" => "Blazor와 .NET 작업 판단을 검증 가능한 지식 로그로 남깁니다.",
            "junho" => "C# 언어 기능과 아키텍처 판단을 목록 있는 로그로 검증합니다.",
            "mina" => "검색 태그, 로그 목록, 공개 UX를 실험하고 남깁니다.",
            _ => "slogs에서 학습과 작업 내용을 지식 로그로 이어 남깁니다."
        };
    }

    private static bool IsLegacyDefaultProfileImageUrl(string? profileImageUrl)
        => !string.IsNullOrWhiteSpace(profileImageUrl)
            && profileImageUrl.StartsWith("https://api.dicebear.com/9.x/initials/svg", StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyDefaultBio(string? bio)
        => bio is "Blazor와 .NET으로 읽기 좋은 개발 글을 정리합니다."
            or "검색, 탐색, 글쓰기 UX를 실험하고 공유합니다."
            or "slogs에서 개발 경험과 학습 기록을 공유합니다."
            or "Blazor와 .NET으로 이어지는 개발 지식 로그를 정리합니다."
            or "C# 언어 기능과 아키텍처 패턴을 기록합니다."
            or "검색, 탐색, 로그 작성 UX를 실험하고 공유합니다."
            or "slogs에서 개발 경험과 학습 내용을 지식 로그로 공유합니다.";

    private static bool IsLegacyReplyFeatureComment(string content)
        => content.Contains(string.Concat("답", "글", " 기능"), StringComparison.Ordinal)
            && content.Contains("더 풍부해질 듯합니다.", StringComparison.Ordinal);

    private static bool IsLegacyCsharpPatternLog(PostRecord post)
        => post.Title == "C# 14의 최신 패턴으로 컴포넌트 정리하기"
            || post.Summary.Contains("서비스와 라우팅 코드를 간결하게 유지", StringComparison.Ordinal)
            || post.Body.StartsWith("# 최신 C#로 정리", StringComparison.Ordinal);

    private static bool IsLegacyCsharpPatternLog(PostRevisionRecord revision)
        => revision.Title == "C# 14의 최신 패턴으로 컴포넌트 정리하기"
            || revision.Summary.Contains("서비스와 라우팅 코드를 간결하게 유지", StringComparison.Ordinal)
            || revision.Body.StartsWith("# 최신 C#로 정리", StringComparison.Ordinal);

    private static void ApplyCsharpPatternLogIdentity(PostRecord post)
    {
        post.Title = CsharpPatternLogTitle;
        post.Summary = CsharpPatternLogSummary;
        post.Body = CsharpPatternLogBody;
        post.ThumbnailUrl = GetDefaultThumbnailUrl(CsharpPatternLogSlug);
    }

    private static void ApplyCsharpPatternLogIdentity(PostRevisionRecord revision)
    {
        revision.Title = CsharpPatternLogTitle;
        revision.Summary = CsharpPatternLogSummary;
        revision.Body = CsharpPatternLogBody;
        revision.ThumbnailUrl = GetDefaultThumbnailUrl(CsharpPatternLogSlug);
    }

    private static string GetDefaultThumbnailUrl(string slug)
    {
        return NormalizeUser(slug) switch
        {
            "blazor-markdown-knowledge-log" or "blazor-markdown-blog" => "https://images.unsplash.com/photo-1516321318423-f06f85e504b3?auto=format&fit=crop&w=900&q=80",
            "modern-csharp-component-patterns" => "https://images.unsplash.com/photo-1555066931-4365d14bab8c?auto=format&fit=crop&w=900&q=80",
            "recall-ux-in-slogs" or "ux-search-in-slogs" => "https://images.unsplash.com/photo-1559028012-481c04fa702d?auto=format&fit=crop&w=900&q=80",
            _ => "https://images.unsplash.com/photo-1498050108023-c5249f4df085?auto=format&fit=crop&w=900&q=80"
        };
    }

    private static string NormalizeUser(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
