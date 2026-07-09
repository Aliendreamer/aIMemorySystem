## ADDED Requirements

### Requirement: Answer "what was declined and why"
The system SHALL answer, for a specified project, what work was declined and the reasons, by retrieving relevant declined decision records and source discussion for that project and summarizing them with a self-hosted local model.

#### Scenario: Declined query returns reasons with citations
- **WHEN** a user asks what was declined and why for project X
- **THEN** the system returns a summary of declined items with their rationale, each backed by a citation to the source item or attachment

#### Scenario: Query is scoped to the project
- **WHEN** a user asks the declined question for project X
- **THEN** only records with `project = X` contribute to the answer

### Requirement: Answer "what are the technical limitations"
The system SHALL answer, for a specified project, what the technical limitations are, by retrieving relevant constraint decision records, ADRs, and docs for that project and summarizing them.

#### Scenario: Limitations query returns constraints with citations
- **WHEN** a user asks what the technical limitations are for project X
- **THEN** the system returns a summary of known limitations, each backed by a citation to the source

### Requirement: Hybrid retrieval with filters
The system SHALL retrieve context using semantic search combined with payload filters (at least `project`, `item_type`, `state`), returning the most relevant chunks for the question.

#### Scenario: Retrieval combines meaning and filters
- **WHEN** a query is issued for a project
- **THEN** retrieval ranks by semantic similarity while restricting results to the requested `project`

### Requirement: Ground answers in citations
Every answer SHALL include citations that identify the source records (and attachments) used, and the system SHALL be explicit when it finds no supporting evidence rather than fabricating an answer.

#### Scenario: No evidence yields an honest empty answer
- **WHEN** a project has no records matching the question
- **THEN** the system states that it found no supporting evidence and returns no fabricated content
