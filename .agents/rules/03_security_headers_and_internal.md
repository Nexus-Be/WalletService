# 03 — SECURITY HEADERS, INTERNAL API & IDENTITY (WALLET SERVICE)

## 1. Public Controllers And Temporary Internal Disable
* All controllers live under `Wallet.Api/Controllers/Public/`. There is no `Controllers/Internal/` folder.
* Internal API security is temporarily disabled. `InternalServiceAuthMiddleware` registration is commented; no route requires `X-Internal-Service-Key`.
* Inter-service calls use plain REST routes: `POST /api/v1/notifications/email/otp`, `POST /api/v1/wallets/checkout/direct`, `POST /api/v1/inventories/initialize`, `POST /api/v1/logs/batch`.
* Re-enabling the old model requires explicit approval and restoring middleware, handler, Gateway blocking, and docs together.

## 3. Trusted Identity Extraction & Ownership Guard
* Never trust client query parameters for `userId`.
* Operations on own wallet (`/api/v1/wallets/me`, `withdrawals`, `transactions`) must extract caller ID using extension helpers:
  ```csharp
  if (!this.TryGetUserId(out var userGuid, out var authError))
      return authError;
  ```
* Users can only access and transact on their own wallet account. Admin role required for `/api/v1/admin/wallets`.
