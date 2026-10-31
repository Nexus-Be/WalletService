# 03 — SECURITY HEADERS, INTERNAL API & IDENTITY (WALLET SERVICE)

## 1. Controller Segregation
* `Wallet.Api/Controllers/Public/`: Serves public wallet actions (`WalletsController`, `AdminWalletsController`).
* `Wallet.Api/Controllers/Internal/`: Serves inter-service balance operations (`InternalWalletsController` at `/api/v1/internal/wallets/checkout/direct`).

## 2. Internal Direct Payment Protection
* Direct balance debiting at `InternalWalletsController` is strictly protected by `InternalServiceAuthMiddleware` with header `X-Internal-Service-Key`.
* Never expose direct debiting endpoints to API Gateway or public internet.

## 3. Trusted Identity Extraction & Ownership Guard
* Never trust client query parameters for `userId`.
* Operations on own wallet (`/api/v1/wallets/me`, `withdrawals`, `transactions`) must extract caller ID using extension helpers:
  ```csharp
  if (!this.TryGetUserId(out var userGuid, out var authError))
      return authError;
  ```
* Users can only access and transact on their own wallet account. Admin role required for `/api/v1/admin/wallets`.
