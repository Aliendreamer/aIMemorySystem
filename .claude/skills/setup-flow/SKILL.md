---
name: setup-flow
description:
  "Use when onboarding a repository or agent to the standard development workflow — installs the required
  skills from the ai.skills store, installs or updates a MANDATORY workflow block in the agent's
  instruction file (CLAUDE.md, AGENTS.md, GEMINI.md, Copilot or Cursor rules), AND configures
  .claude/settings.json with Serena MCP, sandbox, permissions, hooks, and plugins. Use when setting up a
  new repo, switching agents, or refreshing the workflow rules."
type: skill
disable-model-invocation: false
user-invocable: true
tags: [setup, onboarding, workflow, agent-config, development-flow, serena, settings]
agents: [claude, codex, cursor, gemini, copilot]
version: 0.4.0
author: Aliendreamer
---

# Setup Flow

## Overview

Onboards a repository in three parts: **Phase 0** installs the required skills from the ai.skills store
(so the workflow it wires up actually exists — no manual install); **Phase 1** seeds the **agent
instruction file** with a neutral, reusable **`## MANDATORY workflow`** block so every agent in that
repo follows the same loop (`development-flow` for all changes, semantic code tools over raw text
search, post-task skill optimization when there's concrete feedback); **Phase 2** configures
`.claude/settings.json`.

**Core principle: idempotent.** The block is wrapped in markers and inserted-or-replaced — running this
skill again updates the block in place, never duplicates it, and never disturbs the rest of the file.

## When to Use

- Onboarding a new repository, or starting work in a repo that has no workflow rules.
- Switching to or adding a different agent (the rules need to live in that agent's file).
- Refreshing the workflow block after `development-flow` or the conventions change.

**When NOT to use:** for one-off project-specific rules (write those directly in the instruction file);
this skill manages only the shared, neutral workflow block.

## Target file (by agent)

Resolve the instruction file for the active agent. Create it (with a top-level `# <Project>` heading)
if it doesn't exist.

| Agent | Instruction file |
| ----- | ---------------- |
| Claude / Claude Code | `CLAUDE.md` |
| Codex | `AGENTS.md` |
| Gemini CLI | `GEMINI.md` |
| GitHub Copilot | `.github/copilot-instructions.md` |
| Cursor | `.cursor/rules/workflow.md` (or `.cursorrules`) |

If the agent is unknown, default to `CLAUDE.md` (and `AGENTS.md` as a cross-agent fallback), or ask.

## Phase 0 — Install the required skills

**Runs first**, before injecting the workflow block. setup-flow provisions a mandatory skill set from
the ai.skills store so the workflow it wires up actually exists — the user never installs skills by
hand. Idempotent: re-running re-installs/updates each skill in place.

### 0.1 Build the install list

Always install:

- `development-flow` — the canonical workflow skill the block and hook point at.
- `web-security-audit`
- `llm-setup-audit`
- `md-files-audit` — also the baseline reference used by Phase 2.7.

Conditionally install:

- `audit-package-version` — only when the repo root has a `package.json` (JS / Node project).

Optional (opt-in — install when detected, or when the user asks):

- `azure-devops-workflow` — when the repo uses Azure DevOps: an `azure-pipelines.yml`, a
  `.azuredevops/` directory, or a git remote on `dev.azure.com` / `*.visualstudio.com`.

### 0.2 Resolve the target agent

Use the same agent as the Target file table below (`--agent` value): `claude`, `codex`, `cursor`,
`gemini`, or `copilot`. Default to `claude` if unknown.

### 0.3 Install via the store CLI

Run the installer non-interactively — it fetches from the store and drops each skill into the agent's
skills dir (e.g. `.claude/skills/<id>/`):

```bash
npx -y @aliendreamer/ai-skills add development-flow web-security-audit llm-setup-audit \
  md-files-audit [audit-package-version] [azure-devops-workflow] \
  --agent <resolved-agent> --project --yes
```

- Include `audit-package-version` in the id list only when `package.json` was detected in 0.1.
- Include `azure-devops-workflow` only when Azure DevOps was detected in 0.1, or the user opts in.
- `--project` installs into the current repo; use `--global` (`~/`) instead only if the user wants the
  skills available across all their repos.
- `-y` / `--yes` skips prompts (it requires `--agent`). Re-running is safe and idempotent.

### 0.4 Verify & report

Confirm each expected skill folder now exists under the agent's skills dir, and report the list
installed (and anything skipped — e.g. `audit-package-version` on a non-JS repo).

**OpenSpec is not installed here.** `development-flow` treats OpenSpec as optional; if the repo wants
it, set it up separately.

## The block to inject

Insert this verbatim, including the marker comments. It is intentionally **neutral** — no project, org,
stack, or gate count baked in:

```markdown
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
```

## Procedure

1. **Resolve the target file** for the active agent (table above). If it doesn't exist, create it with a
   `# <Project name>` heading first.
2. **Look for the markers** `<!-- setup-flow:start -->` … `<!-- setup-flow:end -->` in the file.
   - **Present:** replace everything between (and including) the markers with the current block.
   - **Absent:** append the block to the end of the file, preceded by one blank line.
3. **Leave all other content untouched** — never reorder, dedupe, or rewrite the rest of the file.
4. **Verify** the file has exactly one `setup-flow:start`/`setup-flow:end` pair and the block reads
   correctly, then report which file you changed.

## Common mistakes

- **Duplicating the block.** Appending without checking for the markers first. Always insert-or-replace.
- **Editing project-specific rules.** This skill owns only the marked block; don't touch the rest.
- **Hardcoding specifics.** Keep the block neutral — no org, stack, repo path, or gate count. Repos add
  their own details outside the markers.
- **Writing to the wrong file.** Match the file to the active agent; don't put `CLAUDE.md` rules in a
  Gemini repo.
- **Renaming the skill reference.** It points at `development-flow` — the canonical skill name.

---

## Phase 2: `.claude/settings.json`

**Claude Code only.** `.claude/settings.json` is read exclusively by Claude Code. Skip Phase 2 when
the active agent is Gemini, Cursor, Copilot, or Codex — Phase 1 (the instruction file) is sufficient
for those agents.

Runs immediately after Phase 1. Configures `.claude/settings.json` for the repo with Serena MCP,
sandbox, permissions, hooks, and plugins. Idempotent on re-run (merge mode preserves existing
customisation).

### 2.1 Existing file check

Check whether `.claude/settings.json` already exists.

- **Absent** → proceed, composing from scratch.
- **Present** → ask the user: **merge** (add missing keys, union lists, preserve existing values)
  or **replace** (overwrite entirely)?

### 2.2 Project type detection

Scan the repo root for these files to determine the sandbox domain and path lists:

| Detected file | Project type | Network domains to add | Filesystem write paths to add |
|---|---|---|---|
| `package.json` | npm / Node | `registry.npmjs.org`, `*.npmjs.org`, `registry.yarnpkg.com`, `github.com`, `codeload.github.com`, `*.githubusercontent.com` | `~/.npm`, `~/.cache`, `~/.local/share/pnpm`, `/usr/local/share/.yarn-global` |
| `*.csproj` or `*.sln` | .NET | `api.nuget.org`, `*.nuget.org`, `github.com`, `codeload.github.com`, `*.githubusercontent.com` | `~/.nuget`, `~/.dotnet` |
| `pyproject.toml`, `setup.py`, or `requirements*.txt` | Python | `pypi.org`, `files.pythonhosted.org`, `github.com`, `codeload.github.com`, `*.githubusercontent.com` | `~/.cache/pip`, `~/.local` |
| `Cargo.toml` | Rust | `crates.io`, `static.crates.io`, `github.com`, `codeload.github.com`, `*.githubusercontent.com` | `~/.cargo` |
| `go.mod` | Go | `proxy.golang.org`, `sum.golang.org`, `github.com`, `codeload.github.com`, `*.githubusercontent.com` | `~/go` |

Multiple types may match (e.g. a repo with both `package.json` and `*.csproj`) — union all matching entries.

**Fallback (nothing detected):** use the curated baseline:

> **Note:** The curated fallback is npm- and Python-biased (covers the most common cases). For Rust
> or Go projects whose manifest is not at the repo root (preventing auto-detection), manually add
> `crates.io`/`~/.cargo` or `proxy.golang.org`/`~/go` entries after setup.

```json
{
  "network": {
    "allowedDomains": [
      "*.githubusercontent.com",
      "*.npmjs.org",
      "codeload.github.com",
      "files.pythonhosted.org",
      "github.com",
      "pypi.org",
      "registry.npmjs.org",
      "registry.yarnpkg.com"
    ]
  },
  "filesystem": {
    "allowWrite": [
      "/usr/local/share/.yarn-global",
      "~/.cache",
      "~/.local/share/pnpm",
      "~/.npm"
    ]
  }
}
```

### 2.3 Prettier detection

Check `package.json` in the repo root:

- `devDependencies` or `dependencies` contains a key starting with `prettier` → **prettier present**.
- OR `scripts` contains any value that references `prettier` → **prettier present**.
- Otherwise → **prettier absent**; skip the PostToolUse hook.

### 2.4 Plugin detection

Discover available plugins by checking (in order):

1. Read `~/.claude/settings.json` → collect keys from its `enabledPlugins` object.
2. List directory entries under `~/.claude/plugins/` if it exists.

Union the two sets into a list of plugin IDs. Present them to the user:

> "Detected plugins: [list]. Which should be enabled? (Select all that apply, or say 'all' / 'none'.)"

Wait for the user's answer before continuing. Build the `enabledPlugins` map: `{ "<id>": true/false }` per the user's selection.

### 2.5 Compose and write `.claude/settings.json`

Assemble the settings object from all gathered inputs and write (or merge into) `.claude/settings.json`.

#### Always-included sections

**`mcpServers`:**

```json
{
  "serena": {
    "command": "uvx",
    "args": [
      "--from",
      "git+https://github.com/oraios/serena",
      "serena",
      "start-mcp-server",
      "--context",
      "claude-code",
      "--project-from-cwd",
      "--open-web-dashboard",
      "false"
    ]
  }
}
```

**`sandbox`** (defaults shown; the root flags are tunable per environment — see the flag guidance
below. Domain/path lists come from 2.2):

```json
{
  "enabled": true,
  "failIfUnavailable": true,
  "allowUnsandboxedCommands": false,
  "network": { "allowedDomains": ["<from 2.2>"] },
  "filesystem": { "allowWrite": ["<from 2.2>"] }
}
```

**Flag guidance — set these to fit the environment the agent runs in.** Ask the user where this repo
runs (local dev, CI, WSL2) if it isn't obvious; default as shown and call out the exceptions:

- **`enabled`** — keep `true`. Disabling removes the protection entirely; prefer widening the `2.2`
  `network`/`filesystem` lists, or `allowUnsandboxedCommands`, over turning the sandbox off.
- **`failIfUnavailable`** — `true` refuses to start Claude Code when the sandbox can't initialise, so
  you never silently run unsandboxed. Keep `true` for local dev on a supported platform. Set
  **`false`** where sandboxing is unavailable or flaky — **CI runners**, **some WSL2 configs**, and
  platforms with no sandbox support — otherwise the agent won't start there.
- **`allowUnsandboxedCommands`** — `false` blocks any command from escaping the sandbox. Keep `false`
  by default. Set **`true`** only when specific tooling genuinely needs it (e.g. build tools that fail
  under the sandbox — some Nx / `tsx` / native-toolchain invocations); the agent then runs such
  commands unsandboxed on approval. Where the agent supports per-command overrides, narrow to the
  offending commands instead of a blanket `true`.

**`permissions`** (fixed baseline — always the same):

```json
{
  "allow": [
    "Bash(git*)",
    "SKILL(*)",
    "mcp__azure-devops__core_list_projects",
    "mcp__azure-devops__search_workitem",
    "mcp__azure-devops__wit_get_work_item",
    "mcp__azure-devops__wit_get_work_item_attachment",
    "mcp__azure-devops__wit_get_work_items_batch_by_ids",
    "mcp__azure-devops__wit_list_work_item_comments",
    "mcp__azure-devops__wit_query_by_wiql",
    "mcp__plugin_serena_serena__*"
  ],
  "deny": [
    "Bash(git branch --delete --force*)",
    "Bash(git branch -D*)",
    "Bash(git push --force*)",
    "Bash(git push -f*)",
    "Bash(git reset --hard*)",
    "Bash(rm -rf*)"
  ]
}
```

**`hooks`** (always included):

> **Note:** `UserPromptSubmit` does not support a `matcher` field — the entry object contains only
> `hooks`. Do not add a `matcher` key here (unlike `PreToolUse` and `PostToolUse` entries).

```json
{
  "UserPromptSubmit": [
    {
      "hooks": [
        {
          "type": "command",
          "command": "echo '{\"hookSpecificOutput\":{\"hookEventName\":\"UserPromptSubmit\",\"additionalContext\":\"MANDATORY dev-flow for code work (.claude development-flow skill), ordered + gated: 1) align via superpowers:brainstorming before touching files; 2) document the change — OpenSpec /opsx:propose for non-trivial work (tiny fixes may skip); 3) implement with TDD (red-green-refactor) — if a web endpoint was added or changed, create/update its .http file; 4) /simplify then /code-review; 5) discover the repo gates from CI/scripts and run build+lint+typecheck+tests; 6) report done/passed/pending; 7) wait for user approval + manual verification before /opsx:archive and commit. Use Serena semantic tools (find_symbol, get_symbols_overview, replace_symbol_body, find_referencing_symbols, search_for_pattern) for ALL code search and edits — never raw grep or hand-editing. Done = gates green, output seen, user verified.\"}}'",
          "statusMessage": "Loading development flow"
        }
      ]
    }
  ],
  "PreToolUse": [
    {
      "matcher": "Grep",
      "hooks": [
        {
          "type": "command",
          "command": "echo '{\"hookSpecificOutput\":{\"hookEventName\":\"PreToolUse\",\"additionalContext\":\"Per CLAUDE.md, prefer Serena (search_for_pattern / find_symbol / find_referencing_symbols) for CODE search; raw grep is fine for configs and non-code.\"},\"suppressOutput\":true}'"
        }
      ]
    },
    {
      "matcher": "Bash",
      "hooks": [
        {
          "type": "command",
          "command": "c=$(jq -r '.tool_input.command // empty' 2>/dev/null); printf '%s' \"$c\" | grep -Eq '(^|[|&; ])(grep|egrep|fgrep|rg)( |$)' && echo '{\"hookSpecificOutput\":{\"hookEventName\":\"PreToolUse\",\"additionalContext\":\"Per CLAUDE.md, prefer Serena (search_for_pattern / find_symbol) for CODE search; raw grep/rg is fine for configs and non-code.\"},\"suppressOutput\":true}'; exit 0"
        }
      ]
    }
  ]
}
```

**PostToolUse prettier hook** (only if prettier detected in 2.3):

```json
{
  "PostToolUse": [
    {
      "matcher": "Write|Edit",
      "hooks": [
        {
          "type": "command",
          "command": "f=$(jq -r '.tool_input.file_path // .tool_response.filePath // empty' 2>/dev/null); case \"$f\" in *.ts|*.tsx|*.js|*.jsx|*.scss|*.css) [ -f \"$f\" ] && ./node_modules/.bin/prettier --ignore-unknown --write \"$f\" >/dev/null 2>&1 ;; esac; exit 0",
          "statusMessage": "Formatting with prettier…"
        }
      ]
    }
  ]
}
```

**`enabledPlugins`** — map from 2.4 with user-selected true/false values.

> **Note:** `SKILL(*)` pre-approves skill invocations. This is supported in Claude Code with the
> superpowers plugin; verify it is supported in your agent before including it.

#### Merge rules (when user chose merge in 2.1)

| Section | Merge behaviour |
|---|---|
| `mcpServers.serena` | Add key only if absent; leave other servers untouched |
| `sandbox.network.allowedDomains` | Union with existing (deduplicate + sort) |
| `sandbox.filesystem.allowWrite` | Union with existing (deduplicate + sort) |
| `sandbox` root flags | Set only if the key is absent; never overwrite |
| `permissions.allow` | Append entries not already present |
| `permissions.deny` | Append entries not already present |
| `hooks` | Add missing hook entries; skip duplicates — for entries with a `matcher`: match on event + matcher + command; for `UserPromptSubmit` (no matcher): match on event + command string |
| `enabledPlugins` | Add new keys from detection; preserve existing values; only update values user explicitly chose |

### 2.6 Report

After writing `.claude/settings.json`, tell the user:

- Path written: `.claude/settings.json`
- Sections written: which top-level keys were added/updated
- Prettier hook: included or skipped (reason)
- Plugins enabled/disabled: the final map
- If merge: which keys already existed and were preserved

### 2.7 Markdown lint detection (optional)

Check whether the repo tracks `.md` files and has `markdownlint-cli2` configured:

- `package.json` scripts contain `markdownlint-cli2` → **markdownlint present**.
- OR `.markdownlint-cli2.jsonc` / `.markdownlintrc.json` exists at repo root → **markdownlint
  present**.
- Otherwise → **markdownlint absent**; skip this step and do not add lint:md scripts.

When markdownlint is present, verify:

1. `lint:md` and `lint:md:fix` scripts exist in `package.json` (add if missing).
2. `.markdownlint-cli2.jsonc` exists and covers the right globs (use **md-files-audit** as the
   reference for a correct baseline config).
3. Run `pnpm lint:md` — if it exits non-zero, run `pnpm lint:md:fix` and re-check.

### Common mistakes (Phase 2)

| Mistake | Fix |
|---|---|
| Writing `PostToolUse` hook when prettier is absent | Check `package.json` first; skip the hook if not found |
| Overwriting existing sandbox flags on merge | Only `set` a root flag if the key is absent |
| Disabling the sandbox to unblock a command | Keep `enabled: true`; widen `network`/`filesystem` or use `allowUnsandboxedCommands` instead |
| Setting `failIfUnavailable: false` everywhere | Keep `true` for local dev; only relax it for CI / WSL2 / unsupported platforms |
| Assuming `.claude/settings.json` lives at repo root | It lives at `.claude/settings.json` relative to the repo |
| Not deduplicating domain/path lists | Use a Set merge; sort the result |
| Auto-enabling all detected plugins | Always ask the user; never auto-enable |
| Writing the PAT or any secret into settings.json | `secrets.json` is the home for secrets; never touch them here |
