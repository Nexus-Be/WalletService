# Shared request and response path

[Back to business use cases](../business-use-cases.md) | [Docs index](../README.md) | [Sequence diagram](00-shared-request-path.diagram.md)

This document traces how HTTP requests, webhooks, and background workers execute inside `WalletService`. Everything described below lives within this repository; optional upstream API Gateway routing, JWT verification, and envelope wrapping occur outside `WalletService`.

## Request lifecycle

```text
Caller (Client via Gateway or Internal S2S or Stripe Webhook)
   │
   ▼
GlobalExceptionHandlerMiddleware   (RFC 9110 application/problem+json on unhandled/domain/validation errors)
   │
   ▼
UserContextMiddleware              (Parses x-user-id GUID, x-user-roles, x-user-email, x-user-permissions)
   │
   ▼
Controller Action Filters          ([RequireRole] / [RequireInternalServiceKey] / [RequireAuctionServiceKey] / [ReleaseFeature])
   │
   ▼
Thin Controller (ISender only)     (Reads route/query/headers, enriches [JsonIgnore] fields, sends MediatR Command/Query)
   │
   ▼
ValidationBehavior<TReq, TRes>     (DataAnnotations on properties AND primary-constructor params + IRequestValidation -> 400 VAL_INVALID_INPUT)
   │
   ▼
Command / Query Handler            (Direct IWalletDbContext, advisory/row locks, domain entities, Stripe/Notification HTTP clients)
   │
   ▼
WalletDbContext.SaveChangesAsync   (Shadow ledger guard + LedgerPostingPending capture when LedgerSettings.Enabled = true)
   │
   ▼
PostgreSQL commit -> Unwrapped DTO (or empty 200 for Stripe webhooks)
```

1. **Exception boundary ([GlobalExceptionHandlerMiddleware](../../Wallet.Api/Middleware/GlobalExceptionHandlerMiddleware.cs)):** Wraps the pipeline and converts exceptions into RFC 9110 `application/problem+json` (`{ type, title, status, errorCode, detail, errors, traceId, timestamp }`).
2. **Identity header parsing ([UserContextMiddleware](../../Wallet.Api/Middleware/UserContextMiddleware.cs)):** WalletService does **not** validate JWT bearer tokens itself; upstream callers/Gateways forward `x-user-id` (must parse as a `Guid` to construct an authenticated `ClaimsPrincipal`), optional CSV `x-user-roles` (lowercased), `x-user-email`, and CSV `x-user-permissions` (uppercased).
3. **Controllers ([WalletsController](../../Wallet.Api/Controllers/Public/WalletsController.cs), [AdminWalletsController](../../Wallet.Api/Controllers/Public/AdminWalletsController.cs)):** Inject only `ISender` and `ILogger`. Server-owned properties (`UserId`, `AdminUserId`, `ActorIsAdmin`, `IdempotencyKey`, route IDs) carry `[JsonIgnore]` on commands/queries and are injected by the controller via `with { ... }`.
4. **Validation ([ValidationBehavior](../../Wallet.Application/Common/Behaviors/ValidationBehavior.cs) & [RequestValidation](../../Wallet.Application/Common/Validation/RequestValidation.cs)):** Validates both standard properties and C# positional `record` constructor parameters (avoiding ASP.NET MVC record-validation gaps) plus `IRequestValidation.ValidateRequest()`, throwing `RequestValidationException` (`400 VAL_INVALID_INPUT`). Invalid JSON model binding is normalized by [ApiValidationProblemFactory](../../Wallet.Api/Common/ApiValidationProblemFactory.cs) without leaking internal parser strings.
5. **Enum serialization ([PascalCaseJsonStringEnumConverter](../../Wallet.Api/Common/Converters/PascalCaseJsonStringEnumConverter.cs)):** Applied to both minimal-API JSON and MVC JSON options with `allowIntegerValues: false`—enums always serialize as `PascalCase` strings (`"Success"`, `"Pending"`, `"RefundFundPayout"`) and integer enum values in request JSON are rejected (`400`).

## Authentication filters & service keys

| Filter / Helper | Applied To | Enforcement Rule | Failure Outcome |
|---|---|---|---|
| `[RequireRole]` ([RequireRoleAttribute](../../Wallet.Api/Filters/RequireRoleAttribute.cs)) | Public buyer/wallet endpoints | Valid non-empty `x-user-id` GUID. | `401 UNAUTHORIZED` |
| `[RequireRole("seller")]` | Seller bank accounts & withdrawals | Valid user + role `"seller"` OR `IsAdmin()` (`"admin"` / `"administrator"`). **`staff` is rejected.** | `401 UNAUTHORIZED` / `403 FORBIDDEN` |
| `[RequireRole("admin", "staff")]` + optional `EnsureAdmin` ([HttpContextExtensions](../../Wallet.Api/Extensions/HttpContextExtensions.cs)) | `AdminWalletsController` | Class allows `admin` or `staff`; 23 admin-only actions additionally enforce `this.EnsureAdmin(...)` (`IsAdmin()`). | `403 FORBIDDEN` |
| `[RequireInternalServiceKey]` ([RequireInternalServiceKeyAttribute](../../Wallet.Api/Filters/RequireInternalServiceKeyAttribute.cs)) | `~/api/v1/internal/...` (except `holds`) + 4 legacy Commerce routes | Compares single header `X-Internal-Service-Key` in constant time (`SHA256` + `FixedTimeEquals`) against `INTERNAL_SERVICE_SECRET` or `InternalSecurity:ServiceKey`. Rejects placeholder secrets. | `500 INTERNAL_CONFIGURATION_ERROR` / `401 UNAUTHORIZED` |
| `[RequireAuctionServiceKey]` ([RequireAuctionServiceKeyAttribute](../../Wallet.Api/Filters/RequireAuctionServiceKeyAttribute.cs)) | `/api/v1/wallets/holds/...` and `~/api/v1/internal/wallets/holds/...` | Same fixed-time `X-Internal-Service-Key` check against `InternalSecurity:AuctionServiceKey` -> `InternalSecurity:ServiceKey` -> `INTERNAL_SERVICE_SECRET`. | `500 INTERNAL_CONFIGURATION_ERROR` / `401 UNAUTHORIZED` |
| `[AllowAnonymous]` | `POST /api/v1/wallets/stripe/webhooks` | Authenticated purely via `Stripe-Signature` HMAC validation + live Stripe API fetch; body capped at `1 MiB` by [StripeWebhookBodyReader](../../Wallet.Api/Common/StripeWebhookBodyReader.cs). | `400 VAL_INVALID_SIGNATURE` / `413 WEBHOOK_PAYLOAD_TOO_LARGE` |

## Release feature flags & toggles

| Gate / Setting | Config Key | Default (`Development`) | Default (Non-Dev) | Scope / Special Rule |
|---|---|---|---|---|
| `ReleaseFeature.Withdrawals` ([ReleaseFeatureAttribute](../../Wallet.Api/Filters/ReleaseFeatureAttribute.cs)) | `ReleaseFeatures:WithdrawalsEnabled` | Open (`true`) | Closed (`false`) | `POST /api/v1/wallets/withdrawals` (new requests only; admin processing & cancel remain open). |
| `ReleaseFeature.AuctionOrderCapture` | `ReleaseFeatures:AuctionOrderCaptureEnabled` | Open (`true`) | Closed (`false`) | `CaptureHold` / `CaptureHoldInternal` **only when `Purpose == "ORDER_PAYMENT"`** (`FORFEIT` bypasses this gate). |
| `ReleaseFeature.LegacyCommerceCredits` | `ReleaseFeatures:LegacyCommerceCreditsEnabled` | Open (`true`) | Closed (`false`) | Reserved legacy credit flag (used as fallback below). |
| `ReleaseFeature.LegacyCommerceSettlement` | `ReleaseFeatures:LegacyCommerceSettlementEnabled` -> falls back to `LegacyCommerceCreditsEnabled` | Open (`true`) | Closed (`false`) | `POST .../sub-orders/{subOrderId}/settle` (both public legacy & internal routes). |
| `ReleaseFeature.OrderSettlementV2` | `ReleaseFeatures:OrderSettlementV2Enabled` | Open (`true`) | Closed (`false`) | `POST ~/api/v1/internal/wallets/order-settlements` and `.../subsidy-hold` (`subsidy-release` & `subsidy-holds/stale` stay ungated). |
| `ReleaseFeature.OrderRefund` | `ReleaseFeatures:OrderRefundEnabled` (no fallback) | **Closed (`false`)** | **Closed (`false`)** | All 4 `RefundOrder` / `RefundSubOrder` routes; closed everywhere unless explicitly `true`. |
| `RefundFundOptions.Enabled` ([RefundFundOptions](../../Wallet.Application/Common/RefundFundOptions.cs)) | `RefundFund:Enabled` | **Closed (`false`)** | **Closed (`false`)** | `PayRefundFundCaseCommandHandler` returns `503 REFUND_FUND_DISABLED` when `false`. |
| `LedgerSettings.Enabled` | DB row `LedgerSettings(Id=1)` via `PUT ledger/shadow` | `false` | `false` | Enables atomic shadow-ledger guard and `LedgerPostingPending` emission on `Balance` changes. |

Unparseable boolean strings on `[ReleaseFeature]` return `500 INTERNAL_CONFIGURATION_ERROR`; disabled features return `503 Service Unavailable` (`FEATURE_DISABLED`, `"This operation is not available."`).

## Error response & pagination contracts

| Exception Type | HTTP Status | `errorCode` | Notes |
|---|---|---|---|
| `StripeWebhookSignatureException` | `400` | `VAL_INVALID_SIGNATURE` | Failed `Stripe-Signature` or livemode check. |
| `StripeWebhookVersionException` | `400` | `VAL_UNSUPPORTED_WEBHOOK_VERSION` | Unparseable Stripe event payload. |
| `RequestValidationException` | `400` | `VAL_INVALID_INPUT` | Includes `errors: { field: [messages] }`. |
| `InsufficientBalanceException` | `400` | `INSUFFICIENT_BALANCE` | Sentence-case message with available vs required amounts. |
| `ArgumentException` | `400` | `VAL_INVALID_ARGUMENT` | Invalid primitive argument. |
| `WalletNotFoundException` | `404` | `WALLET_NOT_FOUND` | Missing wallet (e.g., `AutoCreate = false`, transfer/adjust/freeze-target missing). |
| `EntityNotFoundException` | `404` | `ex.ErrorCode` | e.g., `HOLD_NOT_FOUND`, `WITHDRAWAL_NOT_FOUND`, `BANK_ACCOUNT_NOT_FOUND`. |
| `BusinessRuleValidationException` / `DomainException` | `ex.StatusCode` (`400`/`403`/`404`/`409`/`422`/`503`) | `ex.ErrorCode` | Domain/business-rule conflict or forbidden state. |
| `PaymentProviderUnavailableException` | `503` | `PAYMENT_PROVIDER_UNAVAILABLE` | Stripe HTTP/timeout failure. |
| Unhandled `Exception` | `500` | `INTERNAL_SERVER_ERROR` | Generic safe message; details logged server-side. |

Paged endpoints return `PagedData<T>`: `{ items: [...], pagination: { pageIndex, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage } }` computed directly in PostgreSQL (`Skip`/`Take`/`CountAsync`). Direct callers receive raw DTOs; if routed through an upstream API Gateway, that Gateway may wrap non-error JSON responses in `{ success, statusCode, message, data, traceId, timestamp }`.

## Health endpoint & background workers ([Program.cs](../../Wallet.Api/Program.cs))

- **Readiness probe (`GET /health`):** Runs `db.Database.CanConnectAsync()` -> `200` `{ status: "Healthy", service: "wallet-service" }` or `503` `{ status: "Unhealthy", service: "wallet-service", message }`.
- **Hosted workers:**
  1. [StripeTopupReconciliationWorker](../../Wallet.Api/StripeTopupReconciliationWorker.cs): Polls every **1 minute** (if `StripeSettings.Validate` passes) -> executes `ReconcilePendingTopupsCommand` for stale `CreationPending` (`> 10m`) and `AwaitingPayment` (`> 1m`) Topup and DirectOrder attempts.
  2. [WithdrawalNotificationRetryWorker](../../Wallet.Api/WithdrawalNotificationRetryWorker.cs): Polls every **30 seconds** -> retries undelivered withdrawal notifications up to `8` attempts with exponential backoff (capped at `60m`).
  3. [JournalPostingWorker](../../Wallet.Api/JournalPostingWorker.cs): Polls every **2 seconds** -> ensures seeded `LedgerAccounts` and drains `LedgerPostingPending` (`MaxAttempts = 5`) into balanced `JournalEntries`/`JournalLines`.

## Cross-cutting safety guarantees

- **Available balance vs locked balance:** Debits (`PurchaseDebit`, `TransferOut`, `AdjustmentDebit`, negative operations, seller recovery) and locks (`RequestWithdrawal`, `CreateHold`) check `AvailableBalance = Balance - LockedBalance`; prior reservations cannot be double-spent.
- **Database lock & concurrency primitives ([WalletDbContext](../../Wallet.Infrastructure/Persistence/WalletDbContext.cs)):** Every lock helper requires an active DB transaction and uses `pg_advisory_xact_lock` or `SELECT ... FOR UPDATE` in deterministic ascending ID order across concurrent buyers, sellers, orders, holds, and escrow rows.
- **Shadow ledger chokepoint:** When `LedgerSettings.Enabled = true`, every money mutation transaction takes `FOR SHARE` on `LedgerSettings` and rejects any `WalletAccount.Balance` change lacking a matching `Success` `WalletTransaction` in the same save.
- **Idempotency is explicit, not automatic:** Operations with explicit idempotency keys/records replay safely and reject mismatched payloads with `409`; check each flow document before assuming retry safety.
