## ADDED Requirements

### Requirement: Fetch sources via MCP
The system SHALL ingest content from Azure DevOps and GitHub exclusively through their MCP servers, without hand-rolled REST/GraphQL clients. Each source SHALL be accessed through a common `IConnector` abstraction so additional sources can be added without changing downstream components.

#### Scenario: Ingest a GitHub project
- **WHEN** ingestion runs for a configured GitHub repository
- **THEN** the system fetches its issues (with comments and state history), commit messages, and repository documents via the GitHub MCP server

#### Scenario: Ingest an Azure DevOps project
- **WHEN** ingestion runs for a configured Azure DevOps project
- **THEN** the system fetches its work items (with comments and state history), commit messages, and repository documents via the Azure DevOps MCP server

#### Scenario: Unsupported source is rejected
- **WHEN** ingestion is configured for a source that has no registered connector
- **THEN** the system reports a clear error and ingests nothing for that source

### Requirement: Normalize to a canonical record
The system SHALL normalize every fetched item into a single canonical record schema carrying at minimum: `project`, `source`, `source_id`/`url`, `item_type`, `title`, `text`, normalized `state`, `created`/`updated`, `author`, and `links` (linked work items, PRs, commits, `attachment_of`).

#### Scenario: Work item and issue map to the same schema
- **WHEN** an Azure DevOps work item and a GitHub issue are ingested
- **THEN** both are stored as canonical records with equivalent fields populated and their native identifiers preserved in `source_id`/`url`

#### Scenario: Source state is normalized
- **WHEN** items with source-specific states (e.g. `Removed`, `Closed`, `wontfix`) are ingested
- **THEN** each record carries a normalized `state` value drawn from a fixed vocabulary

### Requirement: Ingest in-repo knowledge artifacts
The system SHALL detect and ingest in-repo knowledge artifacts as canonical records with `item_type = repo_knowledge` and a `doc_kind` tag, covering at least: Serena memories (`.serena/memories/*.md`), agent-instruction files (`CLAUDE.md`, `AGENTS.md`, `GEMINI.md`, `.github/copilot-instructions.md`, `.cursor/rules/*`), and OpenSpec artifacts (`openspec/specs/**`, `openspec/changes/**` including proposal/design/specs/tasks). New `doc_kind` values SHALL be addable without schema changes.

#### Scenario: OpenSpec change is ingested with rationale
- **WHEN** a repository contains an OpenSpec change with `proposal.md` and `design.md`
- **THEN** each file is ingested as a `repo_knowledge` record tagged with the appropriate `doc_kind` (`openspec_proposal`, `openspec_design`, …)

#### Scenario: Serena memory is ingested
- **WHEN** a repository contains `.serena/memories/*.md`
- **THEN** each memory is ingested as a `repo_knowledge` record with `doc_kind = serena_memory`

### Requirement: Chunk text for embedding
The system SHALL split record text into embeddable chunks while preserving each chunk's link back to its parent record and source location.

#### Scenario: Long document is chunked
- **WHEN** a document longer than the configured chunk size is ingested
- **THEN** it is split into multiple chunks, each referencing the same parent `source_id`

### Requirement: Store and link attachments only
The system SHALL download every attachment binary to the blob store and record it as linked to its parent item via `attachment_of`, with metadata (`mime`, `size`, `source_url`, `volume_path`). The system SHALL NOT extract text, perform OCR, or transcribe attachment content in v1.

#### Scenario: Document attachment is stored, not extracted
- **WHEN** a work item has an attached PDF
- **THEN** the PDF binary is saved to the blob store and linked to the work item, and no text is extracted from it

#### Scenario: Media attachment is stored and linked
- **WHEN** an issue has an attached image or video clip
- **THEN** the binary is saved to the blob store and linked to the issue with its metadata, and its contents are not processed
