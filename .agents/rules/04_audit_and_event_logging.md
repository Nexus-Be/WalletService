# 04 — AUDIT LOGGING & FINANCIAL INTEGRITY (WALLET SERVICE)

## 1. Mandatory Audit Logging for Financial Operations
All balance modifications must record audit trails:
* Topup deposits via VNPay (initiation, IPN callback verification, balance crediting).
* Withdrawal requests, approvals, bank transfers, rejections.
* Peer-to-peer transfers (both sender debit and recipient credit).
* Direct order checkout balance deductions.

## 2. Audit Persistence Standards
* Financial entities inherit `AuditableEntity<Guid>` (`CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`).
* Audit rows are written into this service's own `AuditLogs` table through `_dbContext.AddAuditLog(...)` (`Wallet.Application/Common/Extensions/AuditLogExtensions.cs`).
* The audit row is committed in the same database transaction as the balance change.
* Never instantiate `new AuditLog { ... }` outside the extension. Never replace an audit row with `_logger.LogInformation`.
* Never call `LogService` over HTTP inside the business transaction. Rows start with `IsSynced = false`; a background sync may later forward them to `POST /api/v1/internal/logs/batch` with `X-Internal-Service-Key`.
* Audit rows are append-only. INSERT and SELECT only, never UPDATE or DELETE.
* Field reference: `ServiceName`, `Action`, `EntityName`, `EntityId`, `UserId`, `UserName`, `OldValues`, `NewValues`, `ChangedColumns`, `TraceId`, `IpAddress`, `UserAgent`, `CreatedAt`, `IsSynced`.
