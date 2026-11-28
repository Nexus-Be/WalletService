# Feature Specification: Comprehensive Request Validation

**Feature Branch**: `001-comprehensive-request-validation`

**Created**: 2026-11-03

**Status**: Approved for implementation

**Input**: User-approved P1 task 2 from
`docs/operations/production-readiness.md`: add comprehensive request
validation.

## User Scenarios & Testing

### User Story 1 - Reject invalid API inputs consistently (Priority: P1)

An API caller submitting malformed, missing, or out-of-range request data
receives a stable validation response before application handlers perform
business operations or persist data.

**Why this priority**: Consistent boundary validation prevents invalid requests
from causing inconsistent behavior, provider/EF failures, or misleading
responses in financial workflows.

**Independent Test**: Exercise every existing MediatR request with valid and
invalid boundaries; verify invalid requests produce field errors and do not
cause handler-side database changes.

**Acceptance Scenarios**:

1. **Given** a request with an invalid amount, identifier, enum, pagination
   value, date range, or required field, **When** it reaches the application
   validation pipeline, **Then** it returns HTTP 400 problem details with
   `errorCode` `VAL_INVALID_INPUT` and field-level errors.
2. **Given** a malformed HTTP body or query parameter, **When** ASP.NET Core
   model binding rejects it, **Then** it returns the same problem-details
   structure as MediatR validation.
3. **Given** a valid request, **When** it is dispatched, **Then** existing
   route, status, response, and business behavior is preserved.
4. **Given** a malformed or unsigned VNPay callback, **When** it is processed,
   **Then** existing provider callback response-code semantics are preserved.

## API Contract

Invalid DTO/query input and binding errors use HTTP 400 and
`application/problem+json` with `type`, `title`, `status`, `errorCode`,
`detail`, `errors`, `traceId`, and `timestamp`. The error code is
`VAL_INVALID_INPUT`; `errors` maps field names to one or more English
sentence-case messages. Existing routes and valid request contracts remain
unchanged. Invalid pagination is rejected rather than silently clamped.

Handler-level state and provider outcomes remain unchanged, including VNPay
callback response codes and existing business error mappings.

## Edge Cases

- Decimal values must fit the persisted `decimal(18,2)` representation.
- Empty GUIDs and malformed GUID strings are invalid where an identifier is
  required.
- Optional strings remain optional unless current behavior or persistence
  requires a value; known persisted length limits are enforced.
- Page is at least 1, page size is 1 through 100, and `FromDate` is not after
  `ToDate`.
- Empty callback parameter dictionaries retain existing callback response
  semantics; a null dictionary is invalid input.

## Functional Requirements

- **FR-001**: System MUST validate all 11 existing MediatR commands and
  queries before their handlers execute.
- **FR-002**: System MUST collect all request validation errors and associate
  them with their request fields.
- **FR-003**: System MUST return the same problem-details shape for MVC binding
  and MediatR validation failures.
- **FR-004**: Invalid values MUST NOT cause balance changes, ledger writes, or
  audit writes.
- **FR-005**: The implementation MUST preserve the 11-handler/two-controller
  architecture and existing valid API behavior.
- **FR-006**: VNPay callback signatures, processing outcomes, and response-code
  semantics MUST remain unchanged.

## Key Entities

- **Wallet request**: One of the existing 11 commands or queries, including
  client-provided values and server-enriched identity values.
- **Validation problem**: RFC 9110 `application/problem+json` response with a
  stable validation code and field-level error collection.

## Success Criteria

- **SC-001**: Automated tests verify valid and invalid boundaries for every
  existing request type.
- **SC-002**: MVC binding and MediatR validation failures expose identical
  response fields, content type, status, and error code.
- **SC-003**: Invalid request tests demonstrate no handler-side persistence or
  financial side-effects.
- **SC-004**: Architecture verification, Release build, and full test suite
  pass.

## Assumptions

- This work implements validation for the existing use cases only; it does not
  add a handler, route, or endpoint.
- Existing handler and provider behavior remains authoritative for business
  state and callback outcomes.
- Existing project validation conventions and persisted field lengths are the
  source for request constraints; no new arbitrary business limits are added.
