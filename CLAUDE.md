# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

Implementation started. Stack: **.NET 10 / C#**, a modular-monolith solution `AiMemory.slnx` with projects under `src/` (`AiMemory.Core`, `.Connectors`, `.Ingestion`, `.Storage`, `.Ai`, `.Query`, `.Api`) and tests under `tests/AiMemory.Tests` (xUnit). The v1 plan lives in the OpenSpec change `openspec/changes/ai-memory-v1/` (proposal, design, specs, tasks) — read it before working. So far only **Group 1 (scaffolding)** and **Group 2 (core domain + interfaces)** are implemented; Groups 3–11 (storage, AI clients, connectors, ingestion, extraction, query, API, infra, litmus) are pending and need running infra (Qdrant, local model + embedding servers, ADO/GitHub MCP).

### Build / test

```bash
# This WSL2 sandbox blocks the socket()/named-pipe IPC that MSBuild's worker nodes,
# the Roslyn compiler server, and node reuse rely on, so builds MUST run single-proc,
# in-proc, with the build server disabled. Shared-compilation and SCM git queries are
# already turned off in Directory.Build.props.
export DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1

dotnet build AiMemory.slnx -m:1 -nr:false          # quality gate: compile
dotnet test tests/AiMemory.Tests/AiMemory.Tests.csproj --no-restore -m:1 -nr:false   # quality gate: tests
```

NuGet can't reach `api.nuget.org` in the sandbox (NU1900 warnings are harmless); restore works from the populated `~/.nuget` cache. New packages must be added in an environment with network access. Prefer Serena semantic tools over raw grep for code (per the workflow block below).

## What this project is meant to be

`aIMemorySystem` is intended as an **AI-queryable organizational memory** over software projects. The design in `x.drawio` describes the intended data flow — treat it as the source of truth for intent until code supersedes it:

- **Sources (ingest):** repositories (code + in-repo docs, including `.serena`, `.http` files), tickets, and possibly emails — pulled from **Azure DevOps, GitHub, and GitLab**.
- **Connectors:** Azure via REST, GitHub via GraphQL (and REST where needed).
- **Sync/ingest pipeline:** run against repos → sync documentation → ingest and embed.
- **Storage:** a vector/graph store for embeddings — candidates noted in the sketch are **Qdrant** (vectors) and **Neo4j** (graph). Not yet decided.
- **Model:** possibly a **local model** to query the memory.
- **Query output (the product):** for a given project, answer questions like — what development phase it's in, what was *requested* vs *delivered*, what was **declined and why**, and what the **technical limitations** are.

The core value is capturing *decisions and rationale* (declined work + reasons, technical constraints), not just indexing code.

## Notes

- `x.drawio` opens in [diagrams.net / draw.io](https://app.diagrams.net); it is XML and diffable in git.
- Several architecture choices in the diagram are marked with `?` (Qdrant vs Neo4j, local model, whether emails are in scope) — these are open questions, not settled decisions.

<!-- setup-flow:start -->
## MANDATORY workflow

**For ANY feature, change, or bugfix you MUST follow the `development-flow` skill.** Invoke it at the
start of implementation work; do not skip or reorder its steps: brainstorm → plan/proposal →
implement (TDD) → simplify → code review → run the repo's quality gates → report → user approval and
manual verification before archive/commit. If a change adds or modifies a web endpoint, create or
update its `.http` file as part of the same change.

**Prefer semantic code tools for code search and edits** — e.g. Serena MCP or your editor's LSP
(`find_symbol`, `replace_symbol_body`, `find_referencing_symbols`) — over raw text/grep where a
semantic tool applies.

**After finishing a task, optimize skills when there's concrete feedback for it.** If the session
surfaced specific learnings about how a skill performed — an activation gap, a regression, missing or
misleading guidance, or a confirmed improvement — refine that skill (use a skill-optimizing skill if
one is available) before moving on. Only when the feedback is specific; skip it otherwise.
<!-- setup-flow:end -->
