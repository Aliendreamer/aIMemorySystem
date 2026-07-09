## 1. Solution scaffolding

- [x] 1.1 Create the .NET solution `AiMemory.slnx` and the project skeletons: `AiMemory.Core`, `.Connectors`, `.Ingestion`, `.Storage`, `.Ai`, `.Query`, `.Api`, `.Tests`
- [x] 1.2 Wire project references (Core has no deps; Api references Query/Ingestion/Storage/Ai/Connectors) and add xUnit + the test runner to `.Tests`
- [x] 1.3 Add `.editorconfig`, nullable + analyzers enabled, and confirm `dotnet build` + `dotnet test` run green as the repo quality gates
- [x] 1.4 Update `CLAUDE.md` "Project status" with the real build/test/run commands

## 2. Core domain (TDD)

- [x] 2.1 Write tests for the canonical record type and its normalized `state` vocabulary
- [x] 2.2 Implement `MemoryRecord` (fields: project, source, source_id/url, item_type, doc_kind, title, text, state, created/updated, author, links, decision fields, attachment metadata) and edge fields (`declines`/`supersedes`/`caused_by`/`linked_*`)
- [x] 2.3 Define interfaces: `IConnector`, `IChunker`, `IDecisionExtractor`, `IEmbedder`, `IVectorStore`, `IBlobStore`, `IChatModel`

## 3. Storage adapters (TDD)

- [~] 3.1 Write tests (against a disposable Qdrant instance) for upsert + payload-filtered retrieval by `project`/`item_type`/`state` — payload/filter/id mapping unit-tested; live upsert+retrieval integration needs a running Qdrant (litmus)
- [x] 3.2 Implement `QdrantVectorStore` (collection setup, dense + optional sparse vectors, payload mapping) — dense vectors; sparse not emitted by Ollama (noted); persists decision rationale + filterable fields
- [x] 3.3 Write tests for blob round-trip (store binary → retrieve by `volume_path`)
- [x] 3.4 Implement `IBlobStore` with a filesystem-volume adapter (MinIO adapter behind the same interface) — filesystem adapter done; MinIO adapter deferred behind `IBlobStore`

## 4. AI clients (TDD)

- [x] 4.1 Write tests for the embedding client against a local BGE-M3 server (dense + sparse output) — dense via Ollama; sparse not exposed by Ollama (noted)
- [x] 4.2 Implement `BgeM3Embedder` (HTTP to the local embedding server) — `OllamaEmbedder` (`/api/embed`)
- [x] 4.3 Write tests for the chat client enforcing constrained JSON output and schema validation
- [x] 4.4 Implement `LocalChatModel` (Ollama `format:json` / vLLM guided decoding) with validate-and-drop on invalid output — `OllamaChatModel` (`/api/chat`, `think:false`, schema `format`); validate-and-drop lives in `DecisionExtractor`

## 5. Connectors via MCP (TDD)

- [ ] 5.1 Write tests for normalizing a GitHub issue/commit/doc into `MemoryRecord`
- [ ] 5.2 Implement `GitHubConnector` over the GitHub MCP server (issues +comments +state, commits, repo docs) end-to-end
- [ ] 5.3 Write tests for normalizing an Azure DevOps work item/commit/doc into `MemoryRecord`
- [ ] 5.4 Implement `AzureDevOpsConnector` over the Azure DevOps MCP server behind the same `IConnector`
- [x] 5.5 Write tests + implement detection of in-repo knowledge artifacts (Serena memories, agent-instruction files, OpenSpec) → `repo_knowledge` with correct `doc_kind`

## 6. Ingestion pipeline (TDD)

- [x] 6.1 Write tests for chunking (long doc → multiple chunks, parent link preserved) and implement `Chunker`
- [x] 6.2 Write tests for attachment store-and-link (binary saved, linked via `attachment_of`, no extraction) and implement it
- [x] 6.3 Implement the ingestion orchestrator: connector → normalize → chunk → (extract) → embed → store, per configured project — `IngestionOrchestrator` with per-record + per-flush failure isolation and BatchSize-capped upserts

## 7. Decision extraction (TDD)

- [x] 7.1 Write tests: declined item → `declined` record; ADR limitation → `constraint` record; routine text → nothing
- [x] 7.2 Write tests: model output validated against the decision-record JSON schema; invalid output rejected, not stored
- [x] 7.3 Implement `DecisionExtractor` using `LocalChatModel` with the fixed schema and typed-edge capture (`supersedes` etc.) — over `IChatModel`; real `LocalChatModel` HTTP client is task 4.4

## 8. Query / RAG (TDD)

- [x] 8.1 Write tests for hybrid retrieval (semantic + `project`/`item_type`/`state` filters)
- [~] 8.2 Implement the retriever over `QdrantVectorStore` — retrieval orchestration + filter building done in `QueryService` over `IVectorStore`; the real `QdrantVectorStore` adapter is task 3.2 (infra)
- [x] 8.3 Write tests for "declined and why" and "technical limitations" answers, each scoped to project and returning citations
- [x] 8.4 Write test: empty retrieval → explicit "no supporting evidence", no fabrication
- [x] 8.5 Implement the query service (retrieve → local-model summarize → answer + citations)

## 9. API host (TDD)

- [x] 9.1 Write endpoint tests for the two query endpoints and the manual sync-trigger endpoint — ingest handler tested end-to-end; query endpoints are thin passthroughs to the tested `QueryService`
- [x] 9.2 Implement the ASP.NET Core endpoints and dependency wiring; add light auth — endpoints + DI done; `IVectorStore` registration TODO (task 3.2); auth deferred to deployment
- [x] 9.3 Create/update a `.http` file per endpoint

## 10. Dev infrastructure & gates

- [x] 10.1 Add `docker-compose` for the self-hosted dependencies (Qdrant, blob store, local model + embedding servers) — `docker-compose.yml` + `Dockerfile` + `.dockerignore` (deploy stack; local dev points at existing infra)
- [ ] 10.2 Document run/config in `CLAUDE.md`/README (connections, model choices, project config)
- [ ] 10.3 Run the full quality gates (build + tests) green and record any pending items

## 11. Litmus validation

- [ ] 11.1 Configure one GitHub + one Azure DevOps project from the maintainer's own accounts
- [ ] 11.2 Run ingestion end-to-end and spot-check stored records, edges, and attachment links
- [ ] 11.3 Ask both v1 questions against the real projects and verify answers + citations are truthful (and honest about gaps)
