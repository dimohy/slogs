using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public static class SlogsDbInitializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string CsharpPatternLogSlug = "modern-csharp-component-patterns";
    private const string CsharpPatternLogTitle = "C# 14 패턴으로 작업 판단 로그 남기기";
    private const string CsharpPatternLogSummary = "최신 C# 문법을 적용한 이유, 검증 흔적, 리비전 단서를 함께 남기는 작업 로그입니다.";
    private const string CsharpPatternLogBody = "# C# 작업 판단 로그\n\n초기화 구문, 패턴 매칭, 컬렉션 표기법을 적용할 때는 코드량만 줄이는 것이 아니라 선택 이유와 검증 결과를 함께 남겨야 다음 리비전에서 판단을 회상할 수 있습니다.";

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
                "Embedding" vector(768) NOT NULL,
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
            Summary = "서버 렌더링과 인터랙티브 기능을 결합한 Blazor 앱에서 공개 로그 흐름, 단서, 슬로거 회상을 구성하는 방법입니다.",
            Body = "# Blazor 지식 로그 구조\n\n프로젝트를 시작하면 먼저 로그 흐름을 잡고, 데이터 모델을 설계한 뒤 기억과 검증이 이어지는 화면별 컴포넌트를 배치하면 됩니다.",
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
            CreateComment("jane", "대화 흔적 페이지네이션이 필요한 구간이 생길 것 같아요.", now.AddDays(-2).AddHours(-10)),
            CreateComment("kevin", "단서 라우팅 동작은 실제 서비스에서 중요합니다.", now.AddDays(-2).AddHours(-8)),
            CreateComment("rose", "좋은 정렬 기준을 같이 고민하면 유저 피드백이 더 좋아져요.", now.AddDays(-2).AddHours(-6)),
            CreateComment("nate", "문서 정리 방식이 깔끔해서 이해가 빠르네요.", now.AddDays(-2).AddHours(-4)),
            CreateComment("lee", "대화 흔적을 이어 남기는 흐름도 넣으면 더 풍부해질 듯합니다.", now.AddDays(-2).AddHours(-2)),
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
                Title = "slogs 회상 UX를 더 직관적으로 만들기",
                Author = "mina",
                Summary = "회상 입력, 사이드바 단서, 이어지는 로그 추천을 한 번에 정리한 slogs UX 설계 노트입니다.",
                Body = "# 회상 UX\n\n회상은 짧고 명확한 단서(슬로거, 제목, 단서)로 흐름을 좁힐 수 있어야 사용자 편의성이 높습니다.",
                ThumbnailUrl = GetDefaultThumbnailUrl("recall-ux-in-slogs"),
                PublishedAt = now.AddDays(-1),
                UpdatedAt = now,
                Slug = "recall-ux-in-slogs",
                TagsJson = ToJson(["ux", "design", "recall"]),
                SeriesJson = ToJson(["회상 UX 실험실"]),
                ReadTimeMinutes = 7,
                Comments =
                [
                    CreateComment("devin", "좋은 정리입니다. 단서 UX를 강조한 구조가 좋네요.", now.AddHours(-5))
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
            seedPost.Summary = "서버 렌더링과 인터랙티브 기능을 결합한 Blazor 앱에서 공개 로그 흐름, 단서, 슬로거 회상을 구성하는 방법입니다.";
            seedPost.Body = "# Blazor 지식 로그 구조\n\n프로젝트를 시작하면 먼저 로그 흐름을 잡고, 데이터 모델을 설계한 뒤 기억과 검증이 이어지는 화면별 컴포넌트를 배치하면 됩니다.";
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
                    "댓글 페이지네이션이 필요한 구간이 생길 것 같아요." => "대화 흔적 페이지네이션이 필요한 구간이 생길 것 같아요.",
                    "태그 라우팅 동작은 실제 서비스에서 중요합니다." => "단서 라우팅 동작은 실제 서비스에서 중요합니다.",
                    var content when IsLegacyReplyFeatureComment(content) => "대화 흔적을 이어 남기는 흐름도 넣으면 더 풍부해질 듯합니다.",
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
            .FirstOrDefaultAsync(x => x.Author == "mina"
                && (x.Slug == legacySearchSlug || x.Slug == recallUxSlug));
        if (searchPost is not null)
        {
            searchPost.Title = "slogs 회상 UX를 더 직관적으로 만들기";
            searchPost.Summary = "회상 입력, 사이드바 단서, 이어지는 로그 추천을 한 번에 정리한 slogs UX 설계 노트입니다.";
            searchPost.Body = "# 회상 UX\n\n회상은 짧고 명확한 단서(슬로거, 제목, 단서)로 흐름을 좁힐 수 있어야 사용자 편의성이 높습니다.";
            searchPost.TagsJson = ToJson(["ux", "design", "recall"]);
            searchPost.SeriesJson = ToJson(["회상 UX 실험실"]);
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
                    comment.Content = "좋은 정리입니다. 단서 UX를 강조한 구조가 좋네요.";
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
            "junho" => "C# 언어 기능과 아키텍처 판단을 흐름 있는 로그로 검증합니다.",
            "mina" => "회상 단서, 로그 흐름, 공개 공유 UX를 실험하고 남깁니다.",
            _ => "slogs에서 학습과 작업 흐름을 지식 로그로 이어 남깁니다."
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
            or "회상, 탐색, 로그 작성 UX를 실험하고 공유합니다."
            or "slogs에서 개발 경험과 학습 흐름을 지식 로그로 공유합니다.";

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
