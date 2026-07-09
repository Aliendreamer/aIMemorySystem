## ADDED Requirements

### Requirement: Extract structured decision records
The system SHALL analyze ingested text records and emit structured decision records classified as `declined`, `constraint`, or `chosen`, each capturing a `rationale` and, where present, `alternatives_rejected`. Extraction SHALL run against work-item/issue discussions, ADRs/docs, commit messages, and in-repo knowledge artifacts (notably OpenSpec `design.md`/`proposal.md`).

#### Scenario: Declined work item yields a decision record
- **WHEN** a work item closed as won't-fix contains rationale in its discussion
- **THEN** a decision record of type `declined` is produced with the rationale text and a link to the source item

#### Scenario: Constraint captured from an ADR
- **WHEN** an ADR records a technical limitation or trade-off
- **THEN** a decision record of type `constraint` is produced with the limitation described and linked to the ADR

#### Scenario: Text with no decision produces nothing
- **WHEN** a record contains only routine content with no decision or rationale
- **THEN** no decision record is emitted for it

### Requirement: Use self-hosted constrained-JSON generation
Extraction SHALL use a self-hosted small model served locally, invoked in response-only mode (no chain-of-thought), and SHALL constrain the model output to a fixed JSON schema so that every extraction result is machine-parseable. The system SHALL NOT depend on any external or hosted AI API.

#### Scenario: Output conforms to the schema
- **WHEN** the extraction model is invoked for a record
- **THEN** the returned output validates against the decision-record JSON schema before being persisted

#### Scenario: Invalid output is rejected, not stored
- **WHEN** the model returns output that does not validate against the schema
- **THEN** the system rejects the result and does not persist a malformed decision record

### Requirement: Capture typed decision edges
The system SHALL record decision relationships as flat typed fields on the record — at least `declines`, `supersedes`, `caused_by`, and `linked_work_items` — so that relationships are preserved without a graph database and can be promoted to one later without re-extraction.

#### Scenario: Superseding decision is linked
- **WHEN** an ADR states it supersedes an earlier decision
- **THEN** the new decision record's `supersedes` field references the earlier decision's identifier
