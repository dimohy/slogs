using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Slogs.Data.OrganizationMigrations;

[DbContext(typeof(OrganizationDbContext))]
[Migration("20260825064000_OrganizationSemanticIndex")]
public sealed class _20260825064000_OrganizationSemanticIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");
        migrationBuilder.Sql(
            """
            CREATE TABLE organization."OrganizationMemoryEmbeddings" (
                "MemoryId" uuid NOT NULL,
                "OrganizationId" uuid NOT NULL,
                "Model" character varying(80) NOT NULL,
                "Dimensions" integer NOT NULL,
                "ContentHash" character varying(64) NOT NULL,
                "IndexVersion" character varying(80) NOT NULL,
                "Embedding" vector(768) NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_OrganizationMemoryEmbeddings" PRIMARY KEY ("MemoryId"),
                CONSTRAINT "FK_OrganizationMemoryEmbeddings_Memories_MemoryId"
                    FOREIGN KEY ("MemoryId")
                    REFERENCES organization."OrganizationMemories" ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_OrganizationMemoryEmbeddings_Organizations_OrganizationId"
                    FOREIGN KEY ("OrganizationId")
                    REFERENCES organization."Organizations" ("Id")
                    ON DELETE CASCADE
            );

            CREATE INDEX "IX_OrganizationMemoryEmbeddings_Organization_Model_Dimensions"
                ON organization."OrganizationMemoryEmbeddings" ("OrganizationId", "Model", "Dimensions", "IndexVersion");

            CREATE INDEX "IX_OrganizationMemoryEmbeddings_Embedding_Hnsw"
                ON organization."OrganizationMemoryEmbeddings"
                USING hnsw ("Embedding" vector_cosine_ops);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE organization.\"OrganizationMemoryEmbeddings\";");
    }
}
