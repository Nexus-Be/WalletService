# 02 — API ROUTING, ENVELOPES & PAGINATION (WALLET SERVICE)

## 1. Route Conventions in WalletService
* Collections: Lowercase plural nouns (`/api/v1/wallets`, `/api/v1/wallets/me`, `/api/v1/wallets/transactions`, `/api/v1/wallets/withdrawals`, `/api/v1/admin/wallets`).
* Prohibit singular nouns (`/api/v1/wallet`).
* One route attribute per action. Use single-line XML `<summary>`.

## 2. Pure DTO Return & Zero ResultFilter
* Controllers return pure DTOs:
  * `Ok(walletResponse)`
  * `StatusCode(StatusCodes.Status201Created, topupResponse)`
  * `Ok(transactionPageResponse)` (for paginated transaction lists)
* Zero ResultFilter: Do NOT apply `ResultFilter` or manual envelope factories in `Wallet.Api`. Gateway automatically wraps raw DTOs in `ApiResponse<T>`.

## 3. Database Pagination Governance
* Database-level pagination: `.Skip((page - 1) * pageSize).Take(pageSize)` and `.CountAsync()`.
* Output: Structured pagination response containing `items` and pagination metadata.
