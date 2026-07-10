---
name: development-flow
description:
  "Use when starting, implementing, or completing any feature, change, or fix in any software project — at feature
  start, before writing code, and before claiming a change is done, fixed, working, or green. A strict ordered flow
  with a gate per phase; stack-agnostic and repo-agnostic (OpenSpec optional, gates discovered)."
type: skill
disable-model-invocation: false
user-invocable: true
tags: [workflow, tdd, quality, review]
agents: [claude, codex, cursor, gemini, copilot]
version: 0.4.0
author: Aliendreamer
---

# Dev Flow

## Always use semantic code tools

**Use semantic, symbol-aware tools for ALL code search and edits — avoid raw grep/text editing for code where
a semantic tool applies.** This holds in every phase below: finding symbols, reading code, and making edits go
through them (e.g. Serena MCP — `find_symbol`, `get_symbols_overview`, `replace_symbol_body`, `search_for_pattern`,
`find_referencing_symbols` — or your editor's LSP). Activate the project in the tool first if needed.

## Overview

The end-to-end flow for any change. **Never skip steps, never reorder** — each phase has a gate before moving on.
Nothing is "done" until the quality gates pass **and** the user has manually verified it works.

This flow is stack- and repo-agnostic. The discipline (order, gates, evidence) is fixed; the tooling is not —
**OpenSpec is optional** (use it if your repo does), and the quality gates are **discovered per repo**, not
assumed (see Quality Gates).

**Core principle:** No change is "done" on assertion. Done = gates run, output seen, all green, user verified.
Evidence before claims.

## When to Use

- Starting any new feature, module, or fix.
- Before writing implementation code for a change.
- Before claiming a change is "done", "fixed", "working", or "green".
- Before archiving, committing, or merging.

## The Flow (in order)

### 0. Ticket intake (optional)

If the work comes from a tracked ticket / work item (Jira, GitHub issue, Azure DevOps, Linear, …) and you have an
intake skill for that tracker, run it first: it reads the ticket and seeds step 1 with a distilled brief (what +
expected result). For example, `azure-devops-workflow` handles Azure DevOps tickets. No ticket, or no intake skill
→ skip straight to step 1; this step is never a blocker.

### 1. Discuss & align — `superpowers:brainstorming`

When the user raises something to implement, **start by discussing it** with `superpowers:brainstorming`. Explore
intent, requirements, and design. Do **not** touch any file until there is explicit agreement on what to build.

### 2. Document the change — a plan or proposal (OpenSpec optional)

Once aligned, capture the change before coding so scope and tasks are explicit. If your repo uses **OpenSpec**, run
`opsx:propose <change-name>` to create `openspec/changes/<name>/` (proposal, design, specs, tasks). Otherwise use
whatever the repo uses — an issue, a design doc, or a written task list. Keep specs in **one** place; don't scatter
ad-hoc spec files.

### 3. Implement — TDD (+ OpenSpec apply if used)

Implement from the task list with **TDD** (`superpowers:test-driven-development`): write the failing test first,
watch it fail, write minimal code, watch it pass, refactor. If using OpenSpec, drive it with `opsx:apply
<change-name>`. Add a test for every new behavior. Make all code edits with the semantic tools above. If the
change adds or modifies a web endpoint, create/update its `.http` file too (see Web Endpoints).

### 4. Simplify & review

After implementing, in this order:

- **Simplify** — `/simplify` (reuse, simplification, efficiency, altitude). Apply the fixes.
- **Code review** — `superpowers:requesting-code-review` (or `/code-review`), after simplifying.

### 5. Run the quality gates (ENFORCED — see below)

Run **all** the gates the repo defines (discover them — see Quality Gates). Fix every issue found before
reporting complete.

### 6. Report to the user

Summarize: **what is done**, **what passed**, **what still needs checking**. Do not archive or commit yet.

### 7. User approval + manual verification

Wait for the user to confirm they're OK with the change and to **manually verify it works**. Only after explicit
approval: archive the change (`opsx:archive` if using OpenSpec), then commit.

## Web Endpoints → `.http` file

If a change **adds or modifies a web endpoint** (HTTP route, controller action, or handler),
create or update a matching `.http` file so the endpoint is exercisable by hand. `.http` files are
read by VS Code REST Client, JetBrains IDEs, and Visual Studio.

**Convention — one `.http` file per project/service:**

- **Location & name:** `{ProjectName}.http` at the project root, beside its build manifest
  (`.csproj`, `package.json`, etc.). One file per service — not a shared grab-bag.
- **Environments:** keep hosts out of the file. Define them in a sibling `http-client.env.json`
  (or the repo's equivalent) with a `hostAddress`; each file opens `@HostAddress = {{hostAddress}}`
  and builds requests as `{{HostAddress}}/...`, so the same file runs local, docker, or CI.
- **One block per endpoint:** separate requests with `###`, label each `# @name Verb_Route`, and
  order them to mirror the routes. Include required headers (auth, `Content-Type`) and a
  representative body. Reuse `@vars` for tokens and shared paths.

**On change:** new endpoint → add its block; changed endpoint (path, verb, params, request or
response shape) → update the existing block. No web-endpoint change → no `.http` change. The one
predicate: *did the endpoint surface change?*

## Quality Gates (must be GREEN before a change is done)

**Discover the repo's gates — do not assume them.** Find the authoritative list before running anything:

- **CI config is the source of truth** — `.github/workflows/`, `.gitlab-ci.yml`, `azure-pipelines.yml`, etc. The
  checks CI runs are the gates the change must pass.
- **Task runners / scripts** — `package.json` scripts, `Makefile`, `justfile`, `Taskfile.yml`, `nx.json` targets,
  `pyproject.toml`, `Cargo.toml`, `.csproj`/`*.sln`.
- **Pre-commit hooks** — `.pre-commit-config.yaml`, Husky/lefthook configs.

Map those to the usual gate categories and run the ones that apply to what you changed:

| Gate | Look for |
| ---- | -------- |
| Build / compile | `build`, `tsc --noEmit`, `dotnet build -warnaserror`, `cargo build`, `go build` |
| Format | `prettier --check`, `dotnet format --verify-no-changes`, `ruff format --check`, `gofmt` |
| Lint / static analysis | `eslint`, `ruff`, `clippy`, `golangci-lint`, analyzers |
| Tests | `vitest`/`jest`, `pytest`, `dotnet test`, `cargo test`, `go test` |
| Typecheck | `tsc --noEmit`, `mypy`/`pyright` |
| Markdown *(optional)* | `markdownlint-cli2` — run **md-files-audit** if the repo tracks `.md` files |

**Scope to the change.** In a monorepo, prefer an `affected`/changed-scope target (e.g. `nx affected -t lint test
build typecheck`); in a single repo, run the repo's gate scripts. **When in doubt, run the full set CI would run.**
If any command exits non-zero or any test is red, the change is **not done** — fix it.

## The Done Gate (non-negotiable)

A change is done ONLY when ALL of these are true and you have **seen the output**:

- [ ] Every new/changed behavior has a test covering it.
- [ ] If a web endpoint was added or changed, its `.http` file is created/updated.
- [ ] The change was simplified, then reviewed (simplify precedes review).
- [ ] The repo's build + format + lint + typecheck gates are green.
- [ ] The repo's test gate is green.
- [ ] The plan/proposal tasks for the change are checked off.
- [ ] The user has approved and manually verified the change.

## Red Flags — STOP, you are about to violate the flow

- "It's a small change, I'll skip a step." → The flow is ordered and mandatory. Don't skip or reorder.
- "I'll write the test after." → Tests-after prove nothing. Test first (TDD).
- "It builds, so it's done." → Done = all gates + tests green, output seen, user verified.
- "I'm confident it passes." → Confidence is not evidence. Run the gate, read the output.
- "I don't know this repo's gates, I'll just run the build." → Discover the gates from CI/scripts first.
- "I'll grep and hand-edit this code." → Use semantic/symbol-aware tools.
- "Changed the endpoint, the `.http` file can wait." → It's part of this change. Update it now.
- "I'll review it; simplifying is extra." → Simplify first, then review. Both, in that order.
- "I'll archive/commit now." → Not before the user approves and manually verifies.

All of these mean: stop, follow the step, run the gates, and confirm green + approved before saying done.

## Releasing

Releasing is driven by **Conventional Commits** so a tool can derive the next version and changelog. The tool
varies by repo — Nx Release, semantic-release, Changesets, release-please, or a manual tag — so use whatever the
repo configures.

- **Preview first** — run the tool's dry-run (e.g. `pnpm release:dry`, `semantic-release --dry-run`).
- **Release** — bump versions, update the changelog, then commit (e.g. `chore(release): vX.Y.Z`) and tag.
- Release after a meaningful batch of `feat`/`fix` commits is on the main branch and green. Publishing/pushing are
  separate steps — only do them if the repo is configured for them.

## Common Mistakes

| Mistake                                  | Fix                                                  |
| ---------------------------------------- | ---------------------------------------------------- |
| Coding before discuss/align              | Brainstorm → document the change first.              |
| Skipping or reordering steps             | The flow is ordered and mandatory.                   |
| Implementation before a failing test     | TDD: red, then green.                                |
| Assuming gate commands                   | Discover them from CI config / task runners first.   |
| Endpoint added/changed, `.http` untouched | Create/update the `.http` file in the same change.  |
| Reviewing before simplifying             | Simplify first, then review.                         |
| Archiving/committing before approval     | Wait for user approval + manual verification.        |
| Editing code by blind text replace       | Use semantic/symbol-aware tools (`find_symbol`, LSP).|
