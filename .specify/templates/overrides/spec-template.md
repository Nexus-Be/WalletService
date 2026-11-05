# Feature Specification: [FEATURE NAME]

**Feature Branch**: `[###-feature-name]`

**Created**: [DATE]

**Status**: Draft

**Input**: User description: "$ARGUMENTS"

## User Scenarios & Testing

Describe independently testable user journeys, ordered by priority. For each,
include the value delivered, expected behavior, and acceptance scenarios.

### User Story 1 - [Brief Title] (Priority: P1)

[Describe the user journey.]

**Why this priority**: [Value and priority rationale]

**Independent Test**: [How to verify this journey]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

## API Contract

For endpoint changes, specify the HTTP method and exact route, authorization
and ownership requirements, request and response fields, status codes, error
codes/messages, enum values, pagination, idempotency, and compatibility
requirements. Mark inapplicable fields N/A for non-API work. Do not invent a
route or change an existing contract without explicit approval.

## Edge Cases

- [Balance, ownership, authorization, duplicate-request, validation, and
  persistence failure cases relevant to this feature]

## Functional Requirements

- **FR-001**: System MUST [specific capability and observable behavior].
- **FR-002**: System MUST preserve applicable WalletService architecture and
  compatibility constraints from the project constitution.

## Key Entities

- **[Entity]**: [Meaning and relevant attributes or relationships; N/A if none]

## Success Criteria

- **SC-001**: [Measurable, testable outcome]
- **SC-002**: Changed behavior is covered by automated tests and passes the
  repository verification commands.

## Assumptions

- [Explicit assumptions; do not silently decide unresolved contract behavior.]
