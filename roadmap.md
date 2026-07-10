# aIMemorySystem — Roadmap

Status of the v1 build and what comes next. v1 (ingest → extract → embed → Qdrant →
cited query) is implemented and litmus-verified on self-hosted Ollama + Qdrant.

## Ingestion triggers

The pipeline (`RepoKnowledgeScanner` → `IngestionOrchestrator` → Qdrant) is one
idempotent operation — re-running upserts by deterministic point id, so no
duplicates. Everything below is just a different way to *trigger* that same operation.

### 1. Manual — DONE

`POST /ingest/repo { project, repoPath, source }` — on-demand ingest of a local
clone. Stays as the escape hatch for backfills and debugging.

### 2. Scheduled (poll) — PLANNED (phase 1, next)

A `BackgroundService` (`RepoSyncService`) that, on a configurable interval, runs the
ingest for every configured source.

- **Pros:** dead reliable; needs no public ingress (fits a self-hosted tool); handles
  backfill; *is* the reconcile safety net that event-driven sync always needs.
- **Cons:** latency up to the interval; re-does work on unchanged content
  (mitigated later by change-detection, below).
- **Config:** list of sources (project + repo/URL + source kind) + interval
  (default ~30 min). Auth handled by the source MCP servers (they hold the tokens).
- **Idempotent:** deterministic upsert means a full re-sync is safe.

### 3. Webhooks (event-driven) — PLANNED (phase 2, after MCP connectors)

`POST /webhooks/{source}` receives push / PR-merged / issue-closed events and ingests
only the changed items in near-real-time.

- **Pros:** fresh and efficient — only touches what changed; the right model for a
  *living* memory.
- **Cons:** needs a publicly reachable endpoint (the existing reverse proxy can
  provide ingress), per-repo webhook config, and secret validation. Depends on the
  MCP connectors — a webhook only says *what* changed; the item is still fetched via MCP.
- **Design now:** the ingest is structured as a single triggerable operation, so the
  webhook endpoint is a thin add-on over the same pipeline.

### 4. Reconcile — implied by (2)

Webhooks get dropped/missed; the scheduled pass (2) doubles as the periodic reconcile
that guarantees eventual consistency. **Both (2) and (3) ship — event-driven for
freshness, scheduled for correctness.** Order: scheduled first (works immediately),
webhooks once the connectors exist.

## Source connectors

- **In-repo knowledge** (Serena memories, agent files, OpenSpec, ADRs, docs) — DONE.
- **GitLab**, **email** — open questions from the original design; later.

### GitHub / Azure DevOps via MCP — PENDING (5.x), the next code decision

**Decided (early architecture):** ingestion is **MCP-driven, not hand-rolled REST**;
records flow into the persistent store (hybrid). Sources for v1: **GitHub + Azure
DevOps**. **Auth is handled by the MCP servers — they hold the correct tokens**, so
the connector never manages credentials itself.

**What to fetch (per project):** work items / issues (+ comments, + state history),
pull requests, commit messages, and attachments (store-and-link only). Repo docs can
also come via MCP file-read instead of a local clone — reuse
`RepoKnowledgeScanner.Classify` on the fetched paths.

**Open decision — HOW to drive the MCP** (pick when ready):

- **(a) Deterministic MCP-client fetch** — call the MCP server's tools directly from
  .NET (MCP client SDK) with a fixed fetch plan. Fast, predictable, cheap (no LLM in
  the fetch loop); MCP is just transport + auth. **Recommended for bulk/scheduled sync.**
- **(b) Agent-driven fetch** — an LLM agent uses the MCP tools to decide what to pull.
  Flexible for ad-hoc exploration, but slow and token-heavy for bulk backfill.
- Likely answer: **(a) for sync, (b) reserved for interactive queries.**

**Normalization:** map each source object → canonical `MemoryRecord` behind the
existing `IConnector` seam (already defined). Then it feeds the same
`IngestionOrchestrator` and is triggered by manual / scheduled / webhook alike.

**Blocks webhooks:** phase-2 webhooks (above) need these connectors first — an event
says *what* changed; the connector fetches the actual item via MCP.

## Quality & efficiency (v2)

- **Change detection:** skip unchanged files (by content hash / last commit) so
  scheduled syncs don't re-embed everything.
- **Attachment content:** OCR (images) and text extraction (PDF/docx); audio/video
  transcription — currently store-and-link only.
- **Recall / answers:** semantic fallback added; next is tuning the extraction and
  summarization prompts, and evaluating on a *real* project repo (not the tool's own
  spec).
- **Graph store (Neo4j):** only if multi-hop decision-lineage queries prove needed;
  edges are already captured flat, so it's a migration, not a re-extraction.

## Ops

- `docker-compose.yml` (standalone) and `docker-compose.infra.yml` (attach to an
  existing infra network) — DONE.
- Auth on the API endpoints; per-instance concurrency safety on collection init — v2.
