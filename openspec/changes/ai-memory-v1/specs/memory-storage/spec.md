## ADDED Requirements

### Requirement: Embed text with a self-hosted model
The system SHALL generate embeddings for text chunks using a self-hosted BGE-M3 model served locally, and SHALL NOT call any external embedding API. Where the model provides sparse vectors, the system MAY store them to support hybrid search.

#### Scenario: Chunk is embedded locally
- **WHEN** a text chunk is ready for storage
- **THEN** its embedding is produced by the local BGE-M3 server and attached to the chunk before persistence

### Requirement: Persist vectors and payload in Qdrant
The system SHALL store each embedded chunk in Qdrant as a point whose payload includes at least `project`, `source`, `item_type`, `doc_kind` (when applicable), normalized `state`, `links`, decision edge fields, and attachment metadata. Qdrant SHALL be the only datastore for records and vectors; no other database is used in v1.

#### Scenario: Point is filterable by project and type
- **WHEN** chunks from multiple projects and item types are stored
- **THEN** the store supports retrieving points filtered by `project` and `item_type` via payload filters

#### Scenario: Decision edges are queryable payload
- **WHEN** a decision record with typed edges is stored
- **THEN** its `declines`/`supersedes`/`caused_by` values are present in the point payload

### Requirement: Store attachment binaries on a volume
The system SHALL persist attachment binaries in a self-hostable blob store (MinIO or filesystem volume), separate from Qdrant, and record each binary's `volume_path` on its linked record.

#### Scenario: Binary is retrievable from its recorded path
- **WHEN** an attachment has been ingested
- **THEN** its binary is retrievable from the blob store at the `volume_path` stored on the linked record
