# LLM Wiki 1-3 Hop Migration Report

Date: 2026-08-29
Status: reviewed-v3 semantic graph deployed, activated, and verified through live MCP

## Outcome

The previous GraphRAG search was not recursive. It expanded vector seeds through
one shared `NodeKey`, so naming it GraphRAG did not prove distinct two-hop or
three-hop retrieval. The new path keeps one hop as the compatibility default and
allows callers to opt into an actual bounded depth of two or three.

Authoritative memories and Raw Provenance were not converted or rewritten.
Search acceleration was added as disposable derived data:

- 24,975 owner-scoped node-frequency rows;
- 1,799 bounded directed edges;
- maximum outgoing degree 4;
- 2 version-state rows;
- two covering graph-node indexes.

All derived rows can be rebuilt from the existing 53,786 graph nodes and can be
removed without deleting a memory, source prompt, content body, slug, embedding,
or Raw Provenance row.

## Before and after

| Contract | Before | After |
|---|---:|---:|
| Explicit graph depth | One shared-node expansion | 1, 2, or 3 hops |
| Recursive path bound | Not applicable | 8 deep seeds, 4 edges per frontier, depth 3 |
| Cycle handling | No recursive path | Visited-entry path |
| Scope isolation | Owner/category/public filters | Same filters enforced inside every frontier |
| High-frequency work | Repeated node relationship work | Versioned node statistics and bounded derived edges |
| Result diagnostics | Relevance only | Graph depth and graph score |
| Existing memory mutation | Not required | Still not required |
| Backup proof | Deployment dump only | Archive validation, off-host checksum, full restore drill, count comparison |

The first correlated recursive prototype exposed a depth-2 p95 of about
1,280 ms on the restored production corpus. A per-query materialized-frequency
prototype was worse and reached about 9,592 ms. These failed shapes were removed.
The final bounded-edge implementation passed the worst high-frequency-node gate:

| Depth | p50 | p95 | Maximum |
|---:|---:|---:|---:|
| 1 | 164.47 ms | 167.87 ms | 167.87 ms |
| 2 | 145.16 ms | 145.58 ms | 145.58 ms |
| 3 | 159.46 ms | 176.44 ms | 176.44 ms |

On the same frozen production corpus, the derived bounded-edge path reduced the
correlated depth-2 p95 from about 1,280 ms to 145.58 ms (88.63%). Compared with
the rejected 9,592 ms per-query reconstruction shape, current depth 3 is 98.16%
lower at 176.44 ms. These are database-path measurements; deployed MCP
end-to-end latency remains a separate release gate.

## Full read and 1,000-sample recall gate

The gate restored the production backup into a disposable PostgreSQL/pgvector
database and read every authoritative row before sampling:

- memories read: 450/450;
- Raw Provenance rows read: 1,834/1,834;
- authoritative stream SHA-256 before/after:
  `b49e61c0c1988d20d1f02b0a527542ba4d6f362b3d7bc3bde0ba3be80282b029`;
- deterministic samples: 1,000;
- comparison executions: 4,000, frozen legacy plus depths 1, 2, and 3;
- sampled memory present at top 1/5/10: 4,000/4,000 for every threshold;
- MRR and mean rank: 1.0 and 1.0 for legacy and every new depth;
- visible depth-2 candidates: 640 samples;
- visible depth-3 candidates: 620 samples.

| Mode | p50 | p95 | Maximum | MRR |
|---:|---:|---:|---:|---:|
| Frozen legacy | 9.35 ms | 14.99 ms | 188.79 ms | 1.0 |
| 1 hop | 10.30 ms | 15.61 ms | 187.52 ms | 1.0 |
| 2 hops | 8.49 ms | 15.73 ms | 163.35 ms | 1.0 |
| 3 hops | 8.83 ms | 16.15 ms | 178.66 ms | 1.0 |

The first 1,000-sample run surfaced 59 depth-2 candidates but zero depth-3
candidates even though the derived graph contained 1,986 exact shortest-path
depth-3 pairs. Deployment was therefore blocked. Score multipliers alone made
depth-3 visible only outside the MCP top-10 surface. The final opt-in deep-search
path therefore reserves at most one quality-filtered tail candidate for each
enabled deep depth. Primary ordering now uses only the frozen legacy-compatible
base score; graph relation scores select those bounded tail slots but cannot
displace the primary result. This restored exact-target MRR from the rejected
mixed-score prototype to 1.0 at all depths while retaining visible depth-2 and
depth-3 candidates.

## Backup and migration

`scripts/Backup-SlogsDatabase.ps1` creates a PostgreSQL custom-format archive,
validates its archive list, restores it into a disposable database, compares the
four search-corpus table counts, copies the archive off-host, and compares the
remote and local SHA-256 values.

`scripts/Migrate-LlmWikiMultiHop.ps1` requires that backup gate, hashes all
authoritative memory and provenance rows before and after migration, creates
only derived structures, validates the covering indexes, and rejects an edge
out-degree greater than four.

The latest pre-deployment backup is retained remotely under the Slogs backup
directory and off-host under `P:\Backups\Slogs`.

## Reusable verification

- Focused exact-depth and isolation gate:
  `scripts/verify-llm-wiki-multi-hop.ps1`
- Frozen production performance gate:
  `scripts/verify-llm-wiki-production-corpus.ps1 -BackupPath <dump>`
- Full-read 1,000-sample gate:
  `scripts/verify-llm-wiki-production-corpus.ps1 -BackupPath <dump> -Suite RecallSampling -SampleCount 1000`
- Machine-readable results:
  `artifacts/llm-wiki/production-corpus-multihop.json` and
  `artifacts/llm-wiki/legacy-vs-multihop-1000.json`

## Reviewed-v3 semantic graph production result

The final production corpus contains 451 authoritative memories and 1,838 Raw
Provenance rows. Those rows were not rewritten. The active rebuildable graph is
`semantic-reviewed-v3-live`:

- 852 entities;
- 445 memory mentions;
- 917 relations: 827 taxonomy, 75 reviewed `precedes`, and 15 reviewed
  cross-topic relations;
- 2 evidence-backed split proposals;
- manifest SHA-256
  `D94AD054860CBBA59B8ACB5FC2C8F12F3DABCE86F6698C12D6A29C231980B90D`.

The 18-case frozen typed-relation holdout passed 18/18. It fixes the source
entity so relation traversal precision is measured separately from embedding
seed selection. Authenticated production MCP calls independently cover the
end-to-end text-to-embedding-to-graph path.

The final same-process comparison alternated baseline and active modes in four
stable blocks, with 32 warmups and 64 measured samples per mode and depth. The
production SQL was not changed to create the baseline; the test-only baseline
disables the active semantic-version CTE. Maximum actual rows were identical:

| Depth | Baseline p95 | Active p95 | Ratio | Maximum actual rows |
|---:|---:|---:|---:|---:|
| 1 | 11.40 ms | 11.66 ms | 1.023 | 12,682 / 12,682 |
| 2 | 8.78 ms | 8.94 ms | 1.018 | 12,666 / 12,666 |
| 3 | 9.45 ms | 10.74 ms | 1.137 | 12,666 / 12,666 |

All ratios remain below the frozen 1.25 regression threshold. The combined
gate also preserved the authoritative hash and rejected a duplicate semantic
version fail-closed.

Release `20260829145802` was deployed to `https://slogs.dev`. Production has
exactly one active semantic graph, with counts `852/445/917/2`; the app,
PostgreSQL, and EmbeddingGemma containers are running and recent app logs contain
no errors. Authenticated live `search` and `recall` proved:

- one hop keeps direct/default compatibility results;
- two hops returns typed paths such as `precedes > precedes`;
- three hops returns three-edge paths such as
  `inverse:precedes > inverse:precedes > inverse:precedes`;
- the cross-topic Agentic Shaping and LLM Wiki policy relation returns
  `documents` and `inverse:documents` paths.

The observed live recall diagnostics for the two final focused calls were
309 ms and 268 ms. These are end-to-end observations for two calls, not a
statistical latency benchmark. The remaining quality boundary is ongoing
curation: new or materially changed memories require a new reviewed graph
version instead of mutating the active version in place.

The compact machine-readable live verification summary is retained at
`artifacts/llm-wiki/semantic-production-live-verification.json`.
