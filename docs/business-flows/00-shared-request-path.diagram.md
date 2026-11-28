# Shared Request and Response Path — Sequence Diagram

[Detailed walkthrough](00-shared-request-path.md) | [Business overview](../business-use-cases.md) | [Docs index](../README.md)

End-to-end HTTP execution pipeline inside `WalletService` (`GlobalExceptionHandlerMiddleware` -> `UserContextMiddleware` -> authorization and `[ReleaseFeature]` filters -> thin controller `ISender` -> MediatR `ValidationBehavior` -> handler -> `WalletDbContext.SaveChangesAsync` shadow-ledger guard).

```mermaid
sequenceDiagram
    actor Caller as Caller (Gateway / S2S / Stripe)
    participant MW as Middleware (Exception + UserContext)
    participant Filters as Action Filters (Role / Key / Feature)
    participant Ctrl as Thin Controller (ISender)
    participant MediatR as ValidationBehavior + Handler
    participant DB as PostgreSQL (WalletDbContext)

    Note over Caller,DB: == 1. HTTP Entry & Header Context Parsing ==
    Caller->>MW: HTTP Request (x-user-id / x-user-roles / X-Internal-Service-Key / Stripe-Signature)
    MW->>MW: Wrap pipeline in GlobalExceptionHandlerMiddleware
    MW->>MW: UserContextMiddleware parses x-user-id GUID, roles, email, permissions
    MW->>Filters: Dispatch request with ClaimsPrincipal and HttpContext

    Note over Caller,DB: == 2. Authorization & Release Feature Gates ==
    Filters->>Filters: Check RequireRole / RequireInternalServiceKey / RequireAuctionServiceKey / EnsureAdmin
    alt Unauthorized or Forbidden Caller
        Filters-->>MW: 401 UNAUTHORIZED or 403 FORBIDDEN (or 500 INTERNAL_CONFIGURATION_ERROR)
        MW-->>Caller: RFC 9110 application/problem+json
    else ReleaseFeature Gate Disabled
        Filters->>Filters: Check ReleaseFeatures config (Withdrawals / AuctionOrderCapture / OrderSettlementV2 / OrderRefund)
        Filters-->>MW: 503 FEATURE_DISABLED
        MW-->>Caller: RFC 9110 application/problem+json
    else Webhook Payload Too Large or Bad Signature
        Filters-->>MW: 413 WEBHOOK_PAYLOAD_TOO_LARGE or 400 VAL_INVALID_SIGNATURE
        MW-->>Caller: RFC 9110 application/problem+json
    else Authorized & Enabled
        Filters->>Ctrl: Enter controller action

        Note over Caller,DB: == 3. Thin Controller & MediatR Validation ==
        Ctrl->>Ctrl: Enrich JsonIgnore fields via with (UserId / AdminUserId / IdempotencyKey / Route IDs)
        Ctrl->>MediatR: _sender.Send(commandOrQuery)
        MediatR->>MediatR: ValidationBehavior runs DataAnnotations + IRequestValidation
        alt Validation Fails
            MediatR-->>MW: Throw RequestValidationException
            MW-->>Caller: 400 VAL_INVALID_INPUT + field errors
        else Validation Passes
            MediatR->>DB: BeginTransaction / Acquire pg_advisory_xact_lock or FOR UPDATE row locks
            DB-->>MediatR: Locked entities (deterministic ascending ID order)
            MediatR->>MediatR: Execute domain rules (AvailableBalance / EnsureActive / Idempotency / Escrow checks)
            alt Domain or Balance Rule Fails
                MediatR-->>MW: Throw InsufficientBalanceException / WalletNotFoundException / DomainException
                MW-->>Caller: RFC 9110 ProblemDetails (400 / 403 / 404 / 409 / 422 / 503)
            else Business Rules Pass
                Note over Caller,DB: == 4. Persistence & Shadow Ledger Chokepoint ==
                MediatR->>DB: SaveChangesAsync()
                DB->>DB: CaptureLedgerEventsAsync reads LedgerSettings(Id=1) FOR SHARE
                alt LedgerSettings Enabled and Balance Changed Without Success WalletTransaction
                    DB-->>MediatR: Throw InvalidOperationException & roll back tx
                    MW-->>Caller: 500 INTERNAL_SERVER_ERROR
                else Valid Mutation (or Shadow Ledger Off / Locked-Only)
                    opt LedgerSettings Enabled and Balance Changed
                        DB->>DB: Open epoch if needed & insert LedgerPostingPending row
                    end
                    DB-->>MediatR: Commit transaction
                    MediatR-->>Ctrl: Typed DTO or PagedData(T)
                    Ctrl-->>Caller: 200 / 201 / 204 PascalCase Enum JSON DTO (or empty 200 for Stripe webhook)
                end
            end
        end
    end
```
