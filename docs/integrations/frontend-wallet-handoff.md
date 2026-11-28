# WalletService — Frontend Integration Review & Action Items

**Date:** 2026-11-26  
**Audience:** Frontend Team (`FSA-NEXUS-FRONTEND`)  
**Source of truth:** `WalletService` (`WalletsController`, `AdminWalletsController`)

---

## 1. Executive Summary

- **Action required on Frontend:** There are **5 contract/field mismatches** between the current frontend code and `WalletService` that will cause `404` errors or blank UI fields at runtime.
- **Ready to wire (optional):** Several additional `WalletService` endpoints (saved bank accounts, seller withdrawal cancel, wallet transfers, direct checkout status/resume/cancel, admin wallet freeze/ledger/reconciliation/subsidies/refund fund) are already implemented on the backend whenever the UI is ready to expose them.

---

## 2. Required Frontend Fixes (Contract Mismatches)

### Fix 1 (Critical — `404 WALLET_NOT_FOUND`): Admin balance adjustment expects `userId`, not `walletId`

- **Endpoint:** `POST /api/v1/admin/wallets/{userId:guid}/adjustments`
- **Frontend files:**
  - `src/features/admin/services/admin-ledger.service.ts` (`adjustBalance`)
  - `src/features/admin/AdminLedgerScreen.tsx`
- **Problem:** The UI prompts for *"Mã ví điện tử (Wallet ID)"* and passes `walletId` as the path parameter. `WalletService` resolves the wallet by **`userId`** (`Guid`). Passing a `walletId` returns `404 WALLET_NOT_FOUND`.
- **Fix:**
  - Rename the parameter/input to **User ID** (`userId`) and call `POST /api/v1/admin/wallets/${encodeURIComponent(userId)}/adjustments`.
  - Optional: send an `Idempotency-Key` header (1–100 ASCII `[A-Za-z0-9-_]`) to protect against duplicate adjustments on retries.
- **Request body:**
  ```json
  {
    "amount": 500000,
    "adjustmentType": "Credit",
    "reason": "Reconciliation adjustment reference #123"
  }
  ```
  *(Note: `referenceId` is not a field on `AdjustWalletBalanceCommand` and is ignored by the backend.)*
- **Response (`200 OK`):**
  ```json
  {
    "adjustmentId": "guid",
    "walletId": "guid",
    "amount": 500000,
    "balanceAfter": 1500000,
    "createdAt": "2026-11-26T06:00:00+00:00",
    "adjustmentType": "Credit",
    "currency": "Vnd",
    "availableBalance": 1500000
  }
  ```

---

### Fix 2 (High — Blank Dates & Account Holder): Withdrawal list field names

- **Endpoints:**
  - `GET /api/v1/wallets/withdrawals` (Seller)
  - `GET /api/v1/wallets/withdrawals/{id}` (Seller)
  - `GET /api/v1/admin/wallets/withdrawals` (Staff/Admin)
- **Frontend files:**
  - `src/features/wallet/types/index.ts` (`WithdrawalSummary`, `AdminWithdrawal`)
  - `src/features/wallet/SellerWithdrawalForm.tsx`
  - `src/features/staff/StaffWithdrawalsScreen.tsx`
- **Problem:**
  - Backend returns **`createdAt`**, while the frontend reads `requestedAt` (renders *"Chưa có dữ liệu"*).
  - On `AdminWithdrawalResponse`, backend returns **`bankAccountHolder`**, while `StaffWithdrawalsScreen.tsx` reads `accountHolderName` (renders blank).
- **Exact backend response shapes:**
  - **Seller (`WithdrawalSummaryResponse`):**
    ```json
    {
      "withdrawalId": "guid",
      "amount": 100000,
      "fee": 0,
      "netAmount": 100000,
      "bankName": "VCB",
      "maskedAccountNumber": "******7890",
      "accountHolderName": "OFFICIAL SELLER",
      "status": "Pending",
      "statusReason": null,
      "transferReference": null,
      "createdAt": "2026-11-26T06:00:00+00:00",
      "processedAt": null,
      "completedAt": null
    }
    ```
  - **Admin / Staff (`AdminWithdrawalResponse`):**
    ```json
    {
      "withdrawalId": "guid",
      "walletId": "guid",
      "ownerUserId": "guid",
      "amount": 100000,
      "fee": 0,
      "netAmount": 100000,
      "bankName": "VCB",
      "bankAccountNumber": "1234567890",
      "bankAccountHolder": "OFFICIAL SELLER",
      "status": "Pending",
      "statusReason": null,
      "transferReference": null,
      "processedBy": null,
      "createdAt": "2026-11-26T06:00:00+00:00",
      "processedAt": null,
      "completedAt": null
    }
    ```
- **Note:** `POST /api/v1/wallets/withdrawals` (`WithdrawalResponse`) **does** return `requestedAt` — only the `GET` read models use `createdAt`. Also add `Cancelled` to `WITHDRAWAL_STATUS` / `WithdrawalStatusName` so seller-cancelled requests display properly.

---

### Fix 3 (High — Empty Timeline): Withdrawal history schema mismatch

- **Endpoint:** `GET /api/v1/admin/wallets/withdrawals/{id}/history`
- **Frontend files:**
  - `src/features/wallet/types/index.ts` (`WithdrawalHistoryEntry`)
  - `src/features/staff/StaffWithdrawalsScreen.tsx` (`History` component)
- **Problem:** Frontend expects `{ action, fromStatus, toStatus, actorId, occurredAt, reason, transferReference, verificationEvidence }`. Backend returns sanitized audit log entries with a different shape (and intentionally redacts bank details and `VerificationEvidence` for security).
- **Exact backend response (`WithdrawalHistoryEntry[]`):**
  ```json
  [
    {
      "auditId": "guid",
      "action": "WithdrawalApprove",
      "actorUserId": "guid",
      "oldValues": "{\"Status\":\"Pending\"}",
      "newValues": "{\"Status\":\"Approved\",\"AdminNote\":\"...\"}",
      "createdAt": "2026-11-26T06:05:00+00:00"
    }
  ]
  ```
- **Fix:** Update `WithdrawalHistoryEntry` and the `History` timeline renderer to read `auditId`, `action`, `actorUserId`, `createdAt`, and parse `oldValues` / `newValues` JSON when displaying state transitions or notes. Do not expect `verificationEvidence` to be returned in this endpoint.

---

### Fix 4 (Medium — Stale VNPAY Contract & Direct Return URLs): Direct Stripe checkout

- **Endpoint:** `POST /api/v1/wallets/checkout/direct`
- **Frontend files:**
  - `src/features/wallet/services/wallet.service.ts` (`createDirectCheckout`)
  - `src/app/(public)/payments/success/page.tsx`
  - `src/app/(public)/payments/cancel/page.tsx`
- **Problems:**
  1. `wallet.service.ts` still types `createDirectCheckout` with legacy VNPAY fields (`gateway: "VNPAY"`, `bankCode`, `returnUrl`). `WalletService` uses Stripe and only accepts:
     ```json
     {
       "orderId": "guid",
       "amount": 850000
     }
     ```
     Response (`200 OK`): `{ transactionId, orderId, amount, paymentUrl, status, createdAt }`.
  2. `WalletService` automatically appends query parameters to `Stripe:SuccessUrl` and `Stripe:CancelUrl`:
     - **Top-up:** `?kind=topup&transactionId={topupId}`
     - **Direct order:** `?kind=direct&orderId={orderId}`
     Currently `/payments/success` and `/payments/cancel` only forward `transactionId` to the top-up result page and ignore `kind=direct&orderId=...`.
- **Fix:**
  - Update `createDirectCheckout` payload type to `{ orderId: string; amount: number }`.
  - In `/payments/success` and `/payments/cancel`, check `kind` / `orderId`: redirect `kind=direct` (or presence of `orderId`) to `/buyer/payment/result?orderId=...`, and top-ups to `/buyer/wallet/top-up/result?transactionId=...`.
- **Checkout TTL note:** All Stripe Checkout sessions (both top-ups and direct orders) use a fixed **30-minute** expiry.

---

### Fix 5 (Low — Type Alignment): Holds pagination shape

- **Endpoint:** `GET /api/v1/wallets/me/holds`
- **Frontend file:** `src/features/wallet/types/index.ts` (`WalletHoldPage`)
- **Problem:** Frontend types `WalletHoldPage` with flat `{ items, pageIndex, pageSize, totalCount }`. Backend returns the standard `PagedData<HoldListItem>`:
  ```json
  {
    "items": [
      {
        "holdId": "guid",
        "referenceType": "AUCTION_DEPOSIT",
        "referenceId": "string",
        "auctionId": "string | null",
        "amount": 500000,
        "status": "Held",
        "createdAt": "2026-11-26T06:00:00+00:00",
        "resolvedAt": null
      }
    ],
    "pagination": {
      "pageIndex": 1,
      "pageSize": 50,
      "totalCount": 1,
      "totalPages": 1,
      "hasPreviousPage": false,
      "hasNextPage": false
    }
  }
  ```
- **Fix:** Update `WalletHoldPage` to `{ items: WalletHold[]; pagination: Pagination }` (matching `TopupPage` and `WithdrawalPage`).

---

## 3. Outdated Placeholder Copy in Frontend

- `src/features/staff/StaffScreens.tsx` (`StaffQueueScreen`) still states that withdrawal queue and ledger/reconciliation endpoints do not exist. Those endpoints are live on `WalletService` (see Section 4 below).

---

## 4. Available WalletService Endpoints Not Yet Wired in Frontend

All routes below are already implemented on `WalletService` and ready for UI integration if needed:

| Area | Method & Route | Auth | Purpose |
|---|---|---|---|
| **Seller Bank Accounts** | `GET /api/v1/wallets/bank-accounts` | `seller` / `admin` | List saved bank accounts |
| | `POST /api/v1/wallets/bank-accounts` | `seller` / `admin` | Add a saved bank account |
| | `PATCH /api/v1/wallets/bank-accounts/{id}/default` | `seller` / `admin` | Set default bank account |
| | `DELETE /api/v1/wallets/bank-accounts/{id}` | `seller` / `admin` | Delete saved bank account |
| **Seller Withdrawals** | `POST /api/v1/wallets/withdrawals/{id}/cancel` | `seller` / `admin` | Cancel a `Pending` withdrawal & release locked funds |
| **Buyer Wallet** | `GET /api/v1/wallets/transactions/{id}` | Authenticated user | Single transaction detail (with Stripe/Auction ref) |
| | `POST /api/v1/wallets/transfers` | Authenticated user | Wallet-to-wallet transfer (`1,000`–`100,000,000` VND, optional `Idempotency-Key`) |
| **Direct Checkout** | `GET /api/v1/wallets/checkout/direct/{orderId}` | Owner / `admin` / `staff` | Poll direct Stripe checkout status |
| | `POST /api/v1/wallets/checkout/direct/{orderId}/resume` | Owner | Resume open unpaid Stripe direct session |
| | `POST /api/v1/wallets/checkout/direct/{orderId}/cancel` | Owner | Cancel unpaid direct Stripe session |
| **Admin / Staff Ops** | `GET /api/v1/admin/wallets` | `admin` / `staff` | Search & paginate wallets |
| | `GET /api/v1/admin/wallets/{userId}` | `admin` / `staff` | Inspect a user's wallet |
| | `POST /api/v1/admin/wallets/{userId}/freeze` & `/unfreeze` | `admin` / `staff` | Freeze / unfreeze non-system wallet |
| | `GET /api/v1/admin/wallets/holds` & `/{userId}/holds` | `admin` / `staff` | Inspect auction deposit holds |
| **Admin Finance** | `GET /api/v1/admin/wallets/ledger` | `admin` only | Paginated transaction ledger |
| | `GET /api/v1/admin/wallets/reconciliation` | `admin` only | Balance snapshot reconciliation |
| | `GET/PUT/POST /api/v1/admin/wallets/ledger/*` & `reconciliation/*` | `admin` only | Shadow ledger status, journal, trial balance, toggle, backfill, runs & findings |
| | `GET/POST /api/v1/admin/wallets/stripe/*` | `admin` only | List & resolve `ReconciliationRequired` attempts; trigger `reconcile-pending` |
| | `GET/POST /api/v1/admin/wallets/subsidy-budgets/*` | `admin` only | Create, approve (four-eyes), close & list subsidy budgets |
| | `GET/POST /api/v1/admin/wallets/refund-fund` & `/deposits` | `admin` only | Inspect & fund post-payout refund fund |

---

## 5. Quick QA Verification Checklist for Frontend

- [ ] Admin balance adjustment sends `userId` in `/api/v1/admin/wallets/{userId}/adjustments` and succeeds with `200 OK`.
- [ ] Seller withdrawal table and Staff withdrawal queue display `createdAt` dates properly (no *"Chưa có dữ liệu"*).
- [ ] Staff withdrawal queue displays `bankAccountHolder` properly.
- [ ] Staff withdrawal history timeline renders `action`, `actorUserId`, `createdAt`, and parsed `oldValues`/`newValues`.
- [ ] Stripe return routes (`/payments/success` and `/payments/cancel`) route both `?kind=topup&transactionId=...` and `?kind=direct&orderId=...` correctly.
- [ ] `WalletHoldPage` uses `pagination` matching `PagedData<T>`.
