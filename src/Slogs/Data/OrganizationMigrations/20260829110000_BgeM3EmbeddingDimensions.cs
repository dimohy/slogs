using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Slogs.Data.OrganizationMigrations;

[DbContext(typeof(OrganizationDbContext))]
[Migration("20260829110000_BgeM3EmbeddingDimensions")]
public sealed class _20260829110000_BgeM3EmbeddingDimensions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $migration$
            DECLARE
                dimensions integer;
                row_count bigint;
            BEGIN
                SELECT a.atttypmod INTO dimensions
                FROM pg_attribute a
                WHERE a.attrelid = 'organization."OrganizationMemoryEmbeddings"'::regclass
                  AND a.attname = 'Embedding'
                  AND NOT a.attisdropped;

                IF dimensions <> 1024 THEN
                    SELECT COUNT(*) INTO row_count
                    FROM organization."OrganizationMemoryEmbeddings";
                    IF row_count <> 0 THEN
                        RAISE EXCEPTION
                            'OrganizationMemoryEmbeddings contains % legacy vectors. Run the BGE-M3 shadow migration before applying this migration.',
                            row_count;
                    END IF;

                    DROP INDEX IF EXISTS organization."IX_OrganizationMemoryEmbeddings_Embedding_Hnsw";
                    ALTER TABLE organization."OrganizationMemoryEmbeddings"
                        ALTER COLUMN "Embedding" TYPE vector(1024);
                    CREATE INDEX "IX_OrganizationMemoryEmbeddings_Embedding_Hnsw"
                        ON organization."OrganizationMemoryEmbeddings"
                        USING hnsw ("Embedding" vector_cosine_ops);
                END IF;
            END
            $migration$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $migration$
            BEGIN
                IF EXISTS (SELECT 1 FROM organization."OrganizationMemoryEmbeddings") THEN
                    RAISE EXCEPTION 'Cannot downgrade populated BGE-M3 embeddings to 768 dimensions.';
                END IF;
                DROP INDEX IF EXISTS organization."IX_OrganizationMemoryEmbeddings_Embedding_Hnsw";
                ALTER TABLE organization."OrganizationMemoryEmbeddings"
                    ALTER COLUMN "Embedding" TYPE vector(768);
                CREATE INDEX "IX_OrganizationMemoryEmbeddings_Embedding_Hnsw"
                    ON organization."OrganizationMemoryEmbeddings"
                    USING hnsw ("Embedding" vector_cosine_ops);
            END
            $migration$;
            """);
    }
}
