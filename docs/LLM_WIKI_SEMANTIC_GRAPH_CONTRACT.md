# LLM Wiki Semantic Graph Contract

Status: schema and fail-fast validator implemented; full-corpus AI manifest pending
Updated: 2026-08-29

Shared `NodeKey` edges are candidate-discovery signals, not asserted semantic
facts. A meaningful two-hop or three-hop path must be composed from typed,
directed relations whose evidence is present in the frozen memory corpus.

## Required layers

1. Entities normalize people, projects, products, organizations, places,
   events, concepts, decisions, artifacts, and technologies.
2. Mentions bind an entity to an exact current-memory or Raw Provenance quote.
3. Relations connect two entities with a controlled type, direction,
   confidence, and one or more exact evidence quotes.
4. Entry edges are rebuildable projections from verified entity mentions and
   relations. They are not the authority for relation meaning.
5. Split proposals isolate independently recallable meaning when a combined
   memory obscures category, lifecycle, or relation boundaries.

A memory directly mentions its own semantic entity. Category entities are not
also attached as direct mentions because doing so collapses a multi-step
taxonomy into one apparent hop. Memory-to-category and category-to-parent are
explicit `part-of` relations, so every reported hop remains explainable.

## Relation vocabulary

`alias-of`, `same-as`, `part-of`, `depends-on`, `implements`, `documents`,
`supports`, `contradicts`, `supersedes`, `refines`, `caused-by`, `resolves`,
`example-of`, `precedes`, and `related-to`.

`related-to` is a weak last resort and cannot substitute for a more precise
relation. A relation type not in the controlled vocabulary requires a contract
change and fixtures before use.

## AI and deterministic responsibilities

The AI reads the complete frozen corpus, proposes entities, relations, exact
evidence, confidence, and justified split candidates. Deterministic code checks
the corpus SHA-256, owner scope, endpoint existence, vocabulary, confidence
range, duplicate relations, source ownership, and exact quote presence. An
invalid manifest fails as a whole; it is never partially or silently imported.

## Memory splitting

Splitting is allowed when meanings are independently recallable, belong to
different categories, change on different lifecycles, or produce ambiguous
relations when combined. Original memory and Raw Provenance remain intact.
Every activated split records its source memory, evidence range, split version,
and rollback lineage. Split activation must prove no semantic loss, no
unintended duplication, and no legacy recall regression.

## Evaluation

Compatibility accuracy and semantic accuracy are separate gates. The frozen
legacy A/B protects current primary ranking. Semantic evaluation uses manually
reviewed positive and negative relation fixtures, exact path explanations, and
precision/recall counts. A path is not considered correct merely because its
target is present in top 10.
