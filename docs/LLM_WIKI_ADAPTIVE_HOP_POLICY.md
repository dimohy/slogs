# LLM Wiki Adaptive 1/2/3-Hop Policy

Date: 2026-08-29
Status: deployed and production-verified; strict development behavior suite retained with two evaluation-contract misses

## Problem

The MCP search and recall APIs already accepted `maxGraphHops` values from 1
through 3, but the public Agent policy did not tell an Agent how to choose a
depth. Omitting the parameter kept the compatibility default of one hop, while
an Agent could also overcorrect by sending every query through three hops.

The policy and tool contract therefore need one shared rule: select the
smallest depth that can answer the relationship shape in the current request.

## Decision contract

| Request shape | `maxGraphHops` | Required evidence |
|---|---:|---|
| Direct memory, fact, preference, or project context; no relationship chain | 1 | Direct recall candidate |
| One relationship bridge or comparison between memories | 2 | Returned typed `semanticPath` and `graphDepth` |
| Multi-stage causal, provenance, dependency, or chronological chain | 3 | Returned multi-edge typed path |

The Agent does not use three hops merely to increase result count. If expected
context is missing, it first inspects Retrieval Diagnostics and narrows the
query, category scope, or relevance threshold. It widens one step at a time
only when the request actually needs a relationship chain. Relationships not
returned by MCP are never inferred as though they were evidence.

## Compatibility and boundaries

- Omitting `maxGraphHops` remains a one-hop compatibility request.
- Private and public search/recall expose the same depth-selection wording.
- Broad candidate selection without a relationship question remains one hop.
- An ordinary memory request follows capture/find-related/read/merge or
  remember. It does not mutate policy or force three-hop retrieval.
- The active semantic graph remains bounded, owner/public/category isolated,
  and relevance filtered at every depth.

## Evaluation contract

The machine-readable six-case contract is frozen before the first policy run
at `P:\MyWorks\AgenticShaping\evals\slogs-adaptive-hop-cases.json`. It covers
the three depths, evidence-driven progressive widening, broad-search restraint,
and an ordinary-memory negative control. Passing requires 100% shaped expected
actions, 100% scenario and current-task completion, zero forbidden actions,
and no underperformance against the paired baseline.

Text presence is not behavioral proof. Source tests independently verify the
four MCP parameter descriptions and one-hop defaults; isolated model runs
evaluate action selection; authenticated production calls verify actual typed
paths and Retrieval Diagnostics.

## Verification result

- Production policy version: `2026.08.29.2`.
- Production release: `20260829152139`.
- Warning-as-error solution tests: 116 passed, 0 failed, 23 explicitly skipped
  static-copy snapshots.
- Live `tools/list`: private/public search and recall all expose explicit one-hop
  selection, progressive refinement, evidence-bounded widening, and default 1.
- Authenticated production search returned depth 1 for `maxGraphHops: 1`, depths
  1/2 with `documents > inverse:implements` for 2, and depths 2/3 with typed
  three-edge paths for 3.
- Fixed GPT-5.6 Luna Max development suite: shaped 16/18 expected actions
  (88.9%), 4/6 strict scenario passes, 5/6 task guards, 0 forbidden actions;
  paired outcome 4 better, 2 tied, 0 worse than baseline.

The two strict misses are preserved rather than regraded. In the progressive
case, the scenario states that a one-hop call already happened but still grades
selecting `start_with_max_graph_hops_1` as a next action. In broad candidate
selection, the Agent selected the actual small search and explicit one-hop call
but did not select a hypothetical future action to raise depth if a relationship
question later appears. Both generated reasons obeyed the policy, so these are
classified as development evaluation-contract limitations, not observed MCP hop
selection defects. This fixed suite remains unchanged for auditability.

This is one fixed development suite on one model with one run per variant. It
does not establish universal behavior across every Agent or host, and a session
that cached old MCP metadata must reconnect or rediscover tools to see the new
description.
