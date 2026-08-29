namespace Slogs.Tests;

internal static class LlmWikiLegacyGraphSearchCommand
{
    // Frozen from commit 153465004c2768d8497e82b137198d64fa36396f,
    // src/Slogs/Data/LlmWikiService.cs SearchGraphAsync. Only the two trailing
    // diagnostic columns were added so the A/B reader has one result shape.
    internal const string CommandText =
        """
        WITH filtered_entries AS (
            SELECT "Id", "UpdatedAt"
            FROM "LlmWikiEntries"
            WHERE "OwnerUserName" = @owner
              AND (@publicOnly = FALSE OR "IsPublic" = TRUE)
              AND (
                  @categoryPath = ''
                  OR "CategoryPath" = @categoryPath
                  OR "CategoryPath" LIKE @categoryPrefix
              )
        ),
        vector_seed AS (
            SELECT
                e."Id",
                1 - (idx."Embedding" <=> CAST(@queryVector AS vector)) AS vector_score
            FROM filtered_entries AS e
            INNER JOIN "LlmWikiEntryEmbeddings" AS idx
                ON idx."EntryId" = e."Id"
            WHERE idx."OwnerUserName" = @owner
              AND idx."Model" = @model
              AND idx."Dimensions" = @dimensions
            ORDER BY idx."Embedding" <=> CAST(@queryVector AS vector)
            LIMIT @seedLimit
        ),
        query_graph AS (
            SELECT
                nodes."EntryId" AS "Id",
                SUM(nodes."Weight") AS graph_score
            FROM "LlmWikiEntryGraphNodes" AS nodes
            INNER JOIN filtered_entries AS e
                ON e."Id" = nodes."EntryId"
            WHERE nodes."OwnerUserName" = @owner
              AND nodes."NodeKey" = ANY(@queryNodeKeys)
            GROUP BY nodes."EntryId"
        ),
        lexical_match AS (
            SELECT
                nodes."EntryId" AS "Id",
                LEAST(
                    SUM(
                        CASE nodes."NodeType"
                            WHEN 'title-phrase' THEN 0.85
                            WHEN 'title-term' THEN 0.55
                            WHEN 'tag' THEN 0.45
                            WHEN 'category-path' THEN 0.36
                            WHEN 'category-term' THEN 0.30
                            WHEN 'prompt-phrase' THEN 0.28
                            WHEN 'prompt-term' THEN 0.22
                            WHEN 'content-phrase' THEN 0.18
                            WHEN 'content-term' THEN 0.14
                            ELSE 0.0
                        END
                    ),
                    1.15
                ) AS lexical_score
            FROM "LlmWikiEntryGraphNodes" AS nodes
            INNER JOIN filtered_entries AS e
                ON e."Id" = nodes."EntryId"
            WHERE nodes."OwnerUserName" = @owner
              AND nodes."NodeKey" = ANY(@queryNodeKeys)
            GROUP BY nodes."EntryId"
        ),
        seed_graph_nodes AS (
            SELECT nodes."NodeKey"
            FROM "LlmWikiEntryGraphNodes" AS nodes
            INNER JOIN vector_seed AS seed
                ON seed."Id" = nodes."EntryId"
            WHERE nodes."OwnerUserName" = @owner
            GROUP BY nodes."NodeKey"
            ORDER BY MAX(seed.vector_score) DESC, SUM(nodes."Weight") DESC, nodes."NodeKey"
            LIMIT 200
        ),
        expanded_graph AS (
            SELECT
                nodes."EntryId" AS "Id",
                SUM(nodes."Weight") * 0.25 AS graph_score
            FROM "LlmWikiEntryGraphNodes" AS nodes
            INNER JOIN seed_graph_nodes AS seed_nodes
                ON seed_nodes."NodeKey" = nodes."NodeKey"
            INNER JOIN filtered_entries AS e
                ON e."Id" = nodes."EntryId"
            WHERE nodes."OwnerUserName" = @owner
            GROUP BY nodes."EntryId"
        ),
        combined AS (
            SELECT
                "Id",
                vector_score,
                0::double precision AS query_graph_score,
                0::double precision AS expanded_graph_score,
                0::double precision AS lexical_score
            FROM vector_seed
            UNION ALL
            SELECT
                "Id",
                0::double precision AS vector_score,
                graph_score AS query_graph_score,
                0::double precision AS expanded_graph_score,
                0::double precision AS lexical_score
            FROM query_graph
            UNION ALL
            SELECT
                "Id",
                0::double precision AS vector_score,
                0::double precision AS query_graph_score,
                0::double precision AS expanded_graph_score,
                lexical_score
            FROM lexical_match
            UNION ALL
            SELECT
                "Id",
                0::double precision AS vector_score,
                0::double precision AS query_graph_score,
                graph_score AS expanded_graph_score,
                0::double precision AS lexical_score
            FROM expanded_graph
        ),
        ranked AS (
            SELECT
                "Id",
                MAX(vector_score) AS vector_score,
                SUM(query_graph_score) AS query_graph_score,
                SUM(expanded_graph_score) AS expanded_graph_score,
                SUM(lexical_score) AS lexical_score,
                MAX(vector_score) * 0.90
                    + LEAST(SUM(query_graph_score), 16) / 18.0
                    + LEAST(SUM(expanded_graph_score), 10) / 90.0
                    + SUM(lexical_score) AS rank_score
            FROM combined
            GROUP BY "Id"
        ),
        scored AS (
            SELECT
                ranked."Id",
                ranked.rank_score,
                ROUND(LEAST(GREATEST(ranked.rank_score / 1.60, 0), 1) * 100)::integer AS relevance_percent
            FROM ranked
        )
        SELECT scored."Id", scored.relevance_percent, 0 AS graph_depth, 0::double precision AS graph_score
        FROM scored
        INNER JOIN filtered_entries AS e
            ON e."Id" = scored."Id"
        WHERE scored.relevance_percent >= @minRelevancePercent
        ORDER BY scored.rank_score DESC, e."UpdatedAt" DESC
        OFFSET @offset
        LIMIT @limit;
        """;
}
