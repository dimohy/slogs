namespace Slogs.Data;

public static class LlmWikiGraphSearchCommand
{
    public const string GraphIndexVersion = "2026-08-29-multihop-node-frequency-v1";

    public const string CommandText =
"""
            WITH RECURSIVE filtered_entries AS (
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
            graph_seed AS (
                SELECT "Id", vector_score
                FROM vector_seed
                ORDER BY vector_score DESC, "Id"
                LIMIT @graphSeedLimit
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
                INNER JOIN graph_seed AS seed
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
            active_semantic_version AS (
                SELECT "Version"
                FROM "LlmWikiSemanticGraphVersions"
                WHERE "OwnerUserName" = @owner
                  AND "State" = 'active'
                LIMIT 1
            ),
            semantic_walk AS (
                SELECT
                    mentions."EntityKey" AS entity_key,
                    0 AS depth,
                    ARRAY[mentions."EntityKey"]::text[] AS visited,
                    GREATEST(seed.vector_score, 0.05)::double precision AS path_score,
                    ARRAY[]::text[] AS relation_path
                FROM graph_seed AS seed
                INNER JOIN active_semantic_version AS active ON TRUE
                INNER JOIN "LlmWikiSemanticMentions" AS mentions
                    ON mentions."OwnerUserName" = @owner
                   AND mentions."Version" = active."Version"
                   AND mentions."EntryId" = seed."Id"
                WHERE @maxGraphHops > 1
                UNION ALL
                SELECT
                    edge.to_entity_key,
                    walk.depth + 1,
                    walk.visited || edge.to_entity_key,
                    walk.path_score * edge.confidence
                        * CASE WHEN walk.depth = 0 THEN 0.40 ELSE 0.65 END,
                    walk.relation_path || edge.path_label
                FROM semantic_walk AS walk
                INNER JOIN active_semantic_version AS active ON TRUE
                INNER JOIN LATERAL (
                    SELECT directed.to_entity_key, directed.confidence, directed.path_label
                    FROM (
                        SELECT
                            relation."ToEntityKey" AS to_entity_key,
                            relation."Confidence" AS confidence,
                            relation."RelationType" AS relation_type,
                            relation."RelationType" AS path_label
                        FROM "LlmWikiSemanticRelations" AS relation
                        WHERE relation."OwnerUserName" = @owner
                          AND relation."Version" = active."Version"
                          AND relation."State" = 'active'
                          AND relation."FromEntityKey" = walk.entity_key
                        UNION ALL
                        SELECT
                            relation."FromEntityKey" AS to_entity_key,
                            relation."Confidence" AS confidence,
                            relation."RelationType" AS relation_type,
                            'inverse:' || relation."RelationType" AS path_label
                        FROM "LlmWikiSemanticRelations" AS relation
                        WHERE relation."OwnerUserName" = @owner
                          AND relation."Version" = active."Version"
                          AND relation."State" = 'active'
                          AND relation."ToEntityKey" = walk.entity_key
                    ) AS directed
                    ORDER BY
                        CASE WHEN directed.relation_type = 'part-of' THEN 1 ELSE 0 END,
                        directed.confidence DESC,
                        directed.to_entity_key,
                        directed.path_label
                    LIMIT @semanticFanout
                ) AS edge ON TRUE
                WHERE walk.depth < @maxGraphHops
                  AND NOT edge.to_entity_key = ANY(walk.visited)
            ),
            semantic_candidates AS (
                SELECT DISTINCT ON (mention."EntryId")
                    mention."EntryId" AS "Id",
                    walk.depth AS graph_depth,
                    walk.path_score AS graph_score,
                    array_to_string(walk.relation_path, ' > ') AS semantic_path
                FROM semantic_walk AS walk
                INNER JOIN active_semantic_version AS active ON TRUE
                INNER JOIN "LlmWikiSemanticMentions" AS mention
                    ON mention."OwnerUserName" = @owner
                   AND mention."Version" = active."Version"
                   AND mention."EntityKey" = walk.entity_key
                INNER JOIN filtered_entries AS target_entry
                    ON target_entry."Id" = mention."EntryId"
                WHERE walk.depth BETWEEN 1 AND @maxGraphHops
                ORDER BY mention."EntryId", walk.path_score DESC, walk.depth, semantic_path
            ),
            online_semantic_walk AS (
                SELECT
                    seed."Id",
                    0 AS depth,
                    ARRAY[seed."Id"] AS visited,
                    GREATEST(seed.vector_score, 0.05)::double precision AS path_score,
                    ARRAY[]::text[] AS relation_path
                FROM graph_seed AS seed
                WHERE @maxGraphHops > 1
                UNION ALL
                SELECT
                    edge.to_entry_id AS "Id",
                    walk.depth + 1 AS depth,
                    walk.visited || edge.to_entry_id AS visited,
                    walk.path_score * edge.confidence
                        * CASE WHEN walk.depth = 0 THEN 0.55 ELSE 0.70 END,
                    walk.relation_path || edge.path_label
                FROM online_semantic_walk AS walk
                INNER JOIN LATERAL (
                    SELECT directed.to_entry_id, directed.confidence, directed.path_label
                    FROM (
                        SELECT
                            relation."RelatedEntryId" AS to_entry_id,
                            relation."Confidence" AS confidence,
                            CASE WHEN relation."Direction"='outgoing'
                                THEN relation."RelationType"
                                ELSE 'inverse:' || relation."RelationType" END AS path_label
                        FROM "LlmWikiEntrySemanticRelations" AS relation
                        WHERE relation."OwnerUserName"=@owner AND relation."State"='active'
                          AND relation."AnchorEntryId"=walk."Id"
                        UNION ALL
                        SELECT
                            relation."AnchorEntryId" AS to_entry_id,
                            relation."Confidence" AS confidence,
                            CASE WHEN relation."Direction"='outgoing'
                                THEN 'inverse:' || relation."RelationType"
                                ELSE relation."RelationType" END AS path_label
                        FROM "LlmWikiEntrySemanticRelations" AS relation
                        WHERE relation."OwnerUserName"=@owner AND relation."State"='active'
                          AND relation."RelatedEntryId"=walk."Id"
                    ) AS directed
                    ORDER BY directed.confidence DESC, directed.to_entry_id, directed.path_label
                    LIMIT @semanticFanout
                ) AS edge ON TRUE
                INNER JOIN filtered_entries AS neighbor_entry ON neighbor_entry."Id"=edge.to_entry_id
                WHERE walk.depth < @maxGraphHops
                  AND NOT edge.to_entry_id = ANY(walk.visited)
            ),
            online_semantic_candidates AS (
                SELECT DISTINCT ON (walk."Id")
                    walk."Id",
                    walk.depth AS graph_depth,
                    walk.path_score AS graph_score,
                    array_to_string(walk.relation_path, ' > ') AS semantic_path
                FROM online_semantic_walk AS walk
                WHERE walk.depth BETWEEN 1 AND @maxGraphHops
                ORDER BY walk."Id", walk.path_score DESC, walk.depth, semantic_path
            ),
            graph_walk AS (
                SELECT
                    seed."Id",
                    0 AS depth,
                    ARRAY[seed."Id"] AS visited,
                    GREATEST(seed.vector_score, 0.05)::double precision AS path_score
                FROM graph_seed AS seed
                WHERE @maxGraphHops > 1
                UNION ALL
                SELECT
                    edge."ToEntryId" AS "Id",
                    walk.depth + 1 AS depth,
                    walk.visited || edge."ToEntryId" AS visited,
                    walk.path_score
                        * edge."EdgeScore"
                        * CASE WHEN walk.depth = 0 THEN 0.25 ELSE 0.50 END AS path_score
                FROM graph_walk AS walk
                INNER JOIN "LlmWikiGraphEdges" AS edge
                    ON edge."OwnerUserName" = @owner
                   AND edge."FromEntryId" = walk."Id"
                   AND edge."IndexVersion" = @graphIndexVersion
                INNER JOIN filtered_entries AS neighbor_entry
                    ON neighbor_entry."Id" = edge."ToEntryId"
                WHERE walk.depth < @maxGraphHops
                  AND NOT edge."ToEntryId" = ANY(walk.visited)
            ),
            multi_hop_graph AS (
                SELECT
                    walk."Id",
                    MIN(walk.depth) AS graph_depth,
                    MAX(walk.path_score) AS graph_score
                FROM graph_walk AS walk
                WHERE @maxGraphHops > 1
                  AND walk.depth >= 2
                GROUP BY walk."Id"
            ),
            combined AS (
                SELECT
                    "Id",
                    vector_score,
                    0::double precision AS query_graph_score,
                    0::double precision AS expanded_graph_score,
                    0::double precision AS multi_hop_graph_score,
                    0 AS graph_depth,
                    0::double precision AS lexical_score,
                    ''::text AS semantic_path
                FROM vector_seed
                UNION ALL
                SELECT
                    "Id",
                    0::double precision AS vector_score,
                    graph_score AS query_graph_score,
                    0::double precision AS expanded_graph_score,
                    0::double precision AS multi_hop_graph_score,
                    0 AS graph_depth,
                    0::double precision AS lexical_score,
                    ''::text AS semantic_path
                FROM query_graph
                UNION ALL
                SELECT
                    "Id",
                    0::double precision AS vector_score,
                    0::double precision AS query_graph_score,
                    0::double precision AS expanded_graph_score,
                    0::double precision AS multi_hop_graph_score,
                    0 AS graph_depth,
                    lexical_score,
                    ''::text AS semantic_path
                FROM lexical_match
                UNION ALL
                SELECT
                    "Id",
                    0::double precision AS vector_score,
                    0::double precision AS query_graph_score,
                    graph_score AS expanded_graph_score,
                    0::double precision AS multi_hop_graph_score,
                    1 AS graph_depth,
                    0::double precision AS lexical_score,
                    ''::text AS semantic_path
                FROM expanded_graph
                UNION ALL
                SELECT
                    "Id",
                    0::double precision AS vector_score,
                    0::double precision AS query_graph_score,
                    0::double precision AS expanded_graph_score,
                    graph_score AS multi_hop_graph_score,
                    graph_depth,
                    0::double precision AS lexical_score,
                    ''::text AS semantic_path
                FROM multi_hop_graph
                UNION ALL
                SELECT
                    "Id",
                    0::double precision AS vector_score,
                    0::double precision AS query_graph_score,
                    0::double precision AS expanded_graph_score,
                    graph_score AS multi_hop_graph_score,
                    graph_depth,
                    0::double precision AS lexical_score,
                    semantic_path
                FROM semantic_candidates
                UNION ALL
                SELECT
                    "Id",
                    0::double precision AS vector_score,
                    0::double precision AS query_graph_score,
                    0::double precision AS expanded_graph_score,
                    graph_score AS multi_hop_graph_score,
                    graph_depth,
                    0::double precision AS lexical_score,
                    semantic_path
                FROM online_semantic_candidates
            ),
            ranked AS (
                SELECT
                    "Id",
                    MAX(vector_score) AS vector_score,
                    SUM(query_graph_score) AS query_graph_score,
                    SUM(expanded_graph_score) AS expanded_graph_score,
                    SUM(multi_hop_graph_score) AS multi_hop_graph_score,
                    COALESCE(
                        MIN(graph_depth) FILTER (WHERE semantic_path <> ''),
                        MIN(NULLIF(graph_depth, 0)),
                        0
                    ) AS graph_depth,
                    SUM(lexical_score) AS lexical_score,
                    MAX(semantic_path) FILTER (WHERE semantic_path <> '') AS semantic_path,
                    MAX(vector_score) * 0.90
                        + LEAST(SUM(query_graph_score), 16) / 18.0
                        + LEAST(SUM(expanded_graph_score), 10) / 90.0
                        + SUM(lexical_score) AS base_rank_score,
                    MAX(vector_score) * 0.90
                        + LEAST(SUM(query_graph_score), 16) / 18.0
                        + LEAST(SUM(expanded_graph_score), 10) / 90.0
                        + SUM(lexical_score)
                        + LEAST(SUM(multi_hop_graph_score), 1.0) * 6.0 AS relation_rank_score
                FROM combined
                GROUP BY "Id"
            ),
            scored AS (
                SELECT
                    ranked."Id",
                    ranked.base_rank_score,
                    ranked.relation_rank_score,
                    ranked.graph_depth,
                    ranked.expanded_graph_score + ranked.multi_hop_graph_score AS graph_score,
                    COALESCE(ranked.semantic_path, '') AS semantic_path,
                    ROUND(LEAST(GREATEST(ranked.relation_rank_score / 1.60, 0), 1) * 100)::integer AS relevance_percent
                FROM ranked
            ),
            eligible AS (
                SELECT
                    scored.*,
                    e."UpdatedAt",
                    ROW_NUMBER() OVER (
                        ORDER BY scored.base_rank_score DESC, e."UpdatedAt" DESC, scored."Id"
                    ) AS overall_rank,
                    ROW_NUMBER() OVER (
                        PARTITION BY scored.graph_depth
                        ORDER BY scored.relation_rank_score DESC, e."UpdatedAt" DESC, scored."Id"
                    ) AS depth_rank
                FROM scored
                INNER JOIN filtered_entries AS e
                    ON e."Id" = scored."Id"
                WHERE scored.relevance_percent >= @minRelevancePercent
            ),
            output_policy AS (
                SELECT
                    LEAST(@maxGraphHops - 1, GREATEST(@limit - 1, 0)) AS reserved_depth_slots
            ),
            candidate_lanes AS (
                SELECT
                    eligible.*,
                    0 AS output_lane,
                    eligible.overall_rank AS lane_rank
                FROM eligible
                CROSS JOIN output_policy
                WHERE eligible.overall_rank <= @limit - output_policy.reserved_depth_slots
                UNION ALL
                SELECT
                    eligible.*,
                    1 AS output_lane,
                    (@maxGraphHops - eligible.graph_depth)::bigint AS lane_rank
                FROM eligible
                WHERE eligible.graph_depth BETWEEN 2 AND @maxGraphHops
                  AND eligible.depth_rank = 1
                UNION ALL
                SELECT
                    eligible.*,
                    2 AS output_lane,
                    eligible.overall_rank AS lane_rank
                FROM eligible
            ),
            deduplicated_output AS (
                SELECT
                    candidate_lanes.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY candidate_lanes."Id"
                        ORDER BY candidate_lanes.output_lane, candidate_lanes.lane_rank
                    ) AS selected_rank
                FROM candidate_lanes
            )
            SELECT "Id", relevance_percent, graph_depth, graph_score, semantic_path
            FROM deduplicated_output
            WHERE selected_rank = 1
            ORDER BY output_lane, lane_rank, base_rank_score DESC, "UpdatedAt" DESC, "Id"
            OFFSET @offset
            LIMIT @limit;
            """;
}
