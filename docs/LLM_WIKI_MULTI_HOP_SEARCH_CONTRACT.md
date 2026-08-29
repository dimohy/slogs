# LLM Wiki Multi-Hop Search Contract

Status: frozen-production-corpus correctness and performance gates passed; live MCP gate pending
Updated: 2026-08-29

## Current verified shape

`LlmWikiService.SearchGraphAsync` combines vector seeds, direct query-node
matches, lexical matches, and one shared-node expansion. The current
`seed_graph_nodes -> expanded_graph` path connects a seed entry to entries
that share one `NodeKey`. It is not a recursive entry traversal and therefore
does not prove distinct two-hop or three-hop recall.

The candidate implementation now keeps depth 1 as the conservative default
and exposes `maxGraphHops` 1, 2, or 3 through private/public search and recall.
`LlmWikiGraphSearchCommand.CommandText` is the single SQL authority used by the
service and PostgreSQL plan verification. A recursive entry-node-entry walk
uses at most 8 deep-search seeds, fanout 4, maximum depth 3, a visited-entry path,
monotonic 0.25/0.50 hop decay, and logarithmic high-frequency-node
down-weighting. Its recursive anchor is empty at depth 1, so the conservative
default does not pay for unused 2/3-hop traversal. Search results report
`GraphDepth` and `GraphScore`.

Node frequency and the best four outgoing neighbors are materialized as fully
rebuildable derived structures. Recursive frontiers use the bounded edge table
instead of repeating node joins. Recounting the same `NodeKey` or rebuilding
adjacency for every path is forbidden because production-corpus gates exposed
those shapes as depth-2 and depth-3 latency multipliers.

Primary results use the frozen legacy-compatible base score. Graph path scores
decay monotonically and are used only to select at most one quality-filtered
coverage candidate for each enabled deep depth. The coverage candidates occupy
bounded tail slots; they cannot displace the primary result or change its rank.
This separation is required because mixing a relation multiplier into the
primary score made deeper results visible but regressed exact-target MRR. The
1,000-sample comparison gate now forbids that regression.

## Required public behavior

- Search supports an explicit maximum graph depth of 1, 2, or 3.
- A result records the best depth and score contribution so evaluation can
  distinguish vector, lexical, direct graph, and expanded graph retrieval.
- Each additional hop has a monotonic decay and cannot outrank an otherwise
  equal shorter path.
- Traversal is owner-scoped, public/private scoped, category-scoped, cycle-safe,
  deterministic, and bounded before query execution.
- Ordinary search keeps a conservative default; callers opt into deeper
  traversal when a relationship chain is relevant.

## Non-destructive data contract

Existing `LlmWikiEntries`, `LlmWikiEntrySources`, IDs, slugs, content,
category paths, visibility, graph nodes, embeddings, and Raw Provenance are
immutable inputs to this migration. Multi-hop acceleration may add covering
indexes or fully rebuildable derived edges, statistics, and caches. It must not
rewrite or delete source memory data.

Any derived structure must:

1. use foreign keys with cascade only from the authoritative entry;
2. be rebuildable from current graph nodes;
3. support shadow construction and result comparison before activation;
4. allow rollback by dropping only the derived structure;
5. fail closed on version drift instead of silently mixing index generations.

## Bounded traversal and efficiency

- Maximum depth: 3.
- Bound seed entries, neighbors per entry, total visited entries, and graph
  nodes considered per frontier.
- Down-weight high-frequency nodes so generic terms cannot create a dense
  graph explosion.
- Prefer covering indexes for both entry-to-node and node-to-entry directions.
- Deduplicate by entry ID and retain only the best deterministic path.
- Measure database rows visited, query latency percentiles, result overlap, and
  peak frontier size on the same frozen corpus and query set.

## Verification gates

The implementation is incomplete until all of these pass:

1. chain fixtures where the target is reachable only at exactly 1, 2, and 3
   hops;
2. cycle, high-degree-node, owner-isolation, private/public, and category
   negative controls;
3. byte-for-byte preservation of authoritative entries and provenance before
   and after derived-index build;
4. old-versus-new depth-1 result equivalence within the frozen scoring
   contract;
5. `EXPLAIN (ANALYZE, BUFFERS)` evidence that bounded traversal uses the
   intended indexes;
6. repeated latency and DB-work comparison with no regression at depth 1 and
   explicit budgets for depths 2 and 3;
7. rebuild, version-drift failure, and derived-index rollback tests.

## Focused evidence on 2026-08-29

- Disposable pgvector/PostgreSQL 16 executes the production service path and
  proves targets reachable at exactly depth 1, 2, and 3.
- The same fixture proves cycle safety plus owner, public/private, and category
  isolation.
- A before/after serialized snapshot covers authoritative entries and Raw
  Provenance rows; search leaves it byte-identical.
- The default remains one hop. Deeper traversal is opt-in and is bounded to at
  most 8 graph seeds and 4 neighbors per frontier entry. The tighter bound was
  selected after the restored production corpus exposed the original 20-by-8
  frontier as a multi-second high-frequency-node path explosion.
- `scripts/verify-llm-wiki-multi-hop.ps1` owns the disposable database lifecycle
  and never mounts or mutates the existing Slogs PostgreSQL volume.
- `scripts/Backup-SlogsDatabase.ps1` creates a PostgreSQL custom-format archive,
  validates its archive list, restores it into an exact disposable database,
  compares the four search-corpus table counts, copies it off-host, and verifies
  the remote/local SHA-256 value before migration.
- `scripts/Migrate-LlmWikiMultiHop.ps1` adds only rebuildable covering indexes
  with `CREATE INDEX CONCURRENTLY`; it hashes authoritative memory and Raw
  Provenance rows before and after migration and fails if either changes.
- The 2026-08-29 production migration preserved counts
  `450/1834/450/53786` for entries, sources, embeddings, and graph nodes.
  Both authoritative row hashes remained equal before and after the migration,
  and both covering indexes are PostgreSQL `indisvalid=true` and
  `indisready=true`. The verified custom-format archive is retained both under
  the production backup directory and the off-host `P:\Backups\Slogs` root.

The frozen legacy SQL from commit
`153465004c2768d8497e82b137198d64fa36396f` is retained as an independent test
oracle. On 1,000 deterministic samples, legacy and depths 1, 2, and 3 all
produced Hit@1/5/10 of 1,000/1,000 and MRR 1.0. Depths 2 and 3 exposed deep
candidates in 640 and 620 samples respectively. Their p95 latencies were
15.73 ms and 16.15 ms versus legacy 14.99 ms, within the frozen 25% budget.
The gate also proves exact depth-1 result-ID sequence equivalence and preserves
all 450 memories and 1,834 Raw Provenance rows byte-for-byte. Derived node
statistics and bounded edges are versioned and rebuildable; no authoritative
memory data is rewritten. Authenticated deployed MCP verification remains.
