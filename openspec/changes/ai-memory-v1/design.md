## Context

`aIMemorySystem` is greenfield. The goal is a self-hosted, internal-team memory over Azure DevOps + GitHub projects that answers per-project questions about declined work and technical limitations, with citations. The maintainer's own Azure DevOps + GitHub accounts are the litmus test. Two hard constraints shape every decision: (1) **nothing that cannot be self-hosted** — no external/hosted AI APIs; (2) **small, response-only local models** for speed. Ingestion is driven by an agent using the source **MCP servers**, not hand-rolled API clients. See `proposal.md` for motivation and `specs/` for requirements.

## Goals / Non-Goals

**Goals:**
- Answer "what was declined and why" and "what are the technical limitations" per project, with citations.
- Ingest work items/issues (+comments, +state), repo docs/ADRs, commit messages, in-repo knowledge artifacts (Serena memories, agent-instruction files, OpenSpec), and attachment binaries (store-and-link only).
- Run entirely on self-hosted infrastructure; keep the moving-parts count low.
- A modular monolith that can grow (more sources, more query types, attachment extraction, a graph store) without redesign.

**Non-Goals (v1):**
- Requested-vs-delivered join, project-phase analytics, attachment content extraction/OCR/transcription, PR-review-thread ingestion, scheduled/real-time sync, multi-tenancy, and any graph database.

## Decisions

**D1 — Qdrant as the only datastore (vs. Neo4j, or both).** Three of the four flagship queries are filter + semantic over text; the one relational query ("requested vs delivered") is a single hop whose links come free from ADO/GitHub. The genuinely graph-shaped queries (cross-project decision lineage) need *inferred* edges that don't exist in the source — that is an extraction problem, not a storage one. We capture edges as flat typed fields now (`declines`/`supersedes`/`caused_by`), so promoting to Neo4j later is a data migration, not a re-extraction. One store = far less ops/sync burden for an internal tool. *Alternative rejected:* dual Qdrant+Neo4j — premature before multi-hop queries prove their worth.

**D2 — Ingestion via MCP servers, behind `IConnector` (vs. REST/GraphQL clients).** No client code to maintain; ADO MCP is already wired. MCP suits targeted/agentic fetches; the `IConnector` abstraction keeps sources swappable and lets a second source slot in as a mapping, not a new library. *Trade-off:* MCP is a weaker fit for bulk backfill than direct API pagination — acceptable at litmus-test volume; revisit if backfill of large histories becomes slow.

**D3 — Small self-hosted model + constrained JSON for extraction (vs. bigger/hosted model, or free-text parsing).** Extraction reliability comes from *constrained decoding* (guided/grammar JSON via Ollama `format:json` or vLLM guided decoding), not model size or chain-of-thought. Every extraction result validates against a fixed schema before persistence; invalid output is dropped. Satisfies the self-host + small-model constraints. *Alternative rejected:* Azure OpenAI in-tenant — explicitly ruled out by the self-host-only constraint.

**D4 — BGE-M3 embeddings, served locally (vs. in-process ML).** The app never runs a model in-process; it calls local HTTP servers (embeddings via TEI/Infinity/Ollama; generation via Ollama/vLLM). This is what frees the implementation language from Python. BGE-M3 gives long-context multilingual dense vectors plus sparse vectors that Qdrant supports for hybrid search.

**D5 — .NET (C#) modular monolith (vs. Python, vs. JS/TS, vs. microservices).** Because models are served over HTTP, the app is an orchestration + storage + API service — Python's ML edge does not apply. .NET is the maintainer's ecosystem (Azure-native, dotnet tooling already present), every dependency has a first-class .NET client (MCP C# SDK, Qdrant .NET, Microsoft.Extensions.AI/OllamaSharp), and strong typing fits the canonical record and extraction contracts. Projects: `AiMemory.Core` (records + interfaces), `.Connectors` (MCP sources + normalization), `.Ingestion` (fetch/chunk/extract/attachment-link), `.Storage` (Qdrant + blob), `.Ai` (embed + chat clients), `.Query` (RAG), `.Api` (ASP.NET Core host + `.http` files + manual sync trigger), `.Tests` (xUnit). *Alternative rejected:* microservices — pure overhead for an internal v1.

**D6 — Attachments store-and-link only in v1.** Download every binary to a blob store (MinIO or filesystem volume) and link it via `attachment_of`; no extraction/OCR/transcription. Guarantees nothing is dropped while keeping v1 lean; content extraction is a v2 add-on behind the `doc_kind`/attachment metadata already stored.

**D7 — Manual/triggered sync via an API endpoint (vs. scheduled worker).** Keeps v1 to one deployable; a scheduled background Worker is a clean v2 extraction.

## Risks / Trade-offs

- **Small model misses or mis-attributes rationale** → constrained JSON schema, validate-and-drop invalid output, keep the extraction prompt narrow and per-record; measure quality against the maintainer's real projects.
- **MCP bulk-fetch is slow/expensive at scale** → v1 targets 1–2 projects; if large-history backfill is needed, add a direct-API backfill path behind the same `IConnector`.
- **Answer hallucination** → require citations; return an explicit "no supporting evidence" when retrieval is empty (see `project-query` spec).
- **Missing rationale sources** → PR-review threads and verbal decisions are out of scope; the product must be honest about gaps rather than inventing them.
- **Edge data captured flat may still need a graph later** → typed edge fields make that a migration, not a re-extraction.

## Migration Plan

Greenfield — no existing system to migrate. Deployment is docker-compose for the self-hosted dependencies (Qdrant, blob store, local model + embedding servers) plus the .NET service. Rollback = stop the service; no external state is mutated (read-only against sources via MCP).

## Open Questions

- Exact small model(s) for extraction and for query-time summarization (candidates to benchmark on the maintainer's data).
- Serving stack choice: Ollama (simplest) vs vLLM (faster, needs GPU) — decide from available hardware.
- Blob store: MinIO vs plain filesystem volume for v1.
- Chunking parameters (size/overlap) — tune against real documents.
