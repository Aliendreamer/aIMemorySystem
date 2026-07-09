## Why

Software projects lose their *decision history*: what was declined and why, and what the real technical limitations are. That knowledge is scattered across work items, in-repo docs/ADRs, OpenSpec changes, and commit messages, and is not searchable per project. `aIMemorySystem` v1 builds a self-hosted, AI-queryable memory over a team's Azure DevOps + GitHub projects so anyone can ask, for a given project, **what was declined and why** and **what its technical limitations are** — with citations back to the source.

## What Changes

- Establish a greenfield **.NET (C#) modular-monolith solution** — one repo, one `.sln`, focused projects (no microservices).
- **Ingest** from Azure DevOps + GitHub via their **MCP servers** (no hand-rolled REST clients): work items/issues (+comments, +state history), repo docs/ADRs/READMEs, commit messages, and **in-repo knowledge artifacts** (Serena memories, agent-instruction files, OpenSpec proposals/design/specs/tasks).
- **Attachments are store-and-link only** in v1: download every binary to a blob volume and link it to its parent item; **no** text extraction, OCR, or transcription.
- **Extract decision records** from ingested text using a small, self-hosted, response-only (no chain-of-thought) local model with **constrained JSON output**; capture typed edges (`declines`, `supersedes`, `caused_by`, `linked_*`) as flat fields.
- **Embed** text with a self-hosted **BGE-M3** model and store vectors + payload in **Qdrant** (the only datastore; a blob volume holds binaries).
- **Query API** answers the two v1 question types via hybrid retrieval (semantic + payload filter) → local-model summary → answer with citations.
- **Sync is manual/triggered** (an API endpoint), not real-time.

Non-goals for v1 (deferred): requested-vs-delivered join, project-phase analytics, attachment content extraction/OCR/transcription, PR-review-thread ingestion, scheduled/real-time sync, and any graph database.

## Capabilities

### New Capabilities
- `content-ingestion`: Fetch from Azure DevOps + GitHub via MCP, normalize sources (work items/issues, repo docs/ADRs, commit messages, in-repo knowledge artifacts) into a canonical record schema, chunk text, and store-and-link attachment binaries.
- `decision-extraction`: Turn ingested text into structured decision records (declined/constraint/chosen) with rationale and typed edges, using a self-hosted small model with constrained JSON decoding.
- `memory-storage`: Embed text with self-hosted BGE-M3 and persist vectors + payload (project, source, item_type, state, links, edge fields, attachment metadata) in Qdrant, plus a blob store for attachment binaries.
- `project-query`: Answer, per project, "what was declined and why" and "what are the technical limitations" via hybrid retrieval and local-model summarization, returning answers with citations to source items/attachments.

### Modified Capabilities
<!-- None — greenfield; no existing specs. -->

## Impact

- **New solution and stack**: .NET 10 / C#, ASP.NET Core Web API; projects `AiMemory.Core`, `.Connectors`, `.Ingestion`, `.Storage`, `.Ai`, `.Query`, `.Api`, `.Tests`.
- **New runtime dependencies (all self-hostable)**: Qdrant; a blob volume (MinIO or filesystem); a local model server (Ollama or vLLM) for generation with constrained JSON; a local embedding server for BGE-M3 (TEI/Infinity/Ollama); Azure DevOps + GitHub MCP servers.
- **Hard constraint**: nothing that cannot be self-hosted — no external/hosted AI APIs.
- **Litmus test**: the maintainer's own Azure DevOps + GitHub accounts/projects.
- **Auth/scale**: internal team tool — light auth, no multi-tenancy.
