# 06 — Synchronous Service Integration Guide

This guide is for the Gateway, User, Commerce, and future Auction repositories integrating with WalletService. It describes the current HTTP contracts and separates implemented behavior from integration guidance. WalletService uses synchronous REST over HTTP/JSON; callers wait for a response, while server-side I/O remains asynchronous.

## 1. Service boundaries and current integration status

| Service | Current relationship to WalletService | Integration responsibility |
|---|---|---|
| API Gateway | Wallet routes are implemented, but Gateway routing is not verified in this repository. | Authenticate the caller, overwrite the forwarded `x-user-id`/`x-user-roles` headers, proxy the supported client-facing Wallet routes, and preserve the status code and response. Do not expose the order-payment, settlement, or refund routes as frontend operations. |
| UserService | Wallet does not validate JWTs and makes no synchronous UserService lookup. It trusts the gateway-forwarded `x-user-id` and `x-user-roles` headers. | Ensure the gateway-forwarded user ID is the same stable user ID used by Commerce orders. |
| CommerceService | Order-payment, seller-settlement, and order/sub-order refund routes are implemented. | Call Wallet after reserving inventory; only mark the order paid after a successful Wallet response. On a confirmed business failure, release the reservation. See the [integration contract](../docs/api/integration-contract.md) for settle and refund. |
| AuctionService | No auction-specific Wallet endpoint or integration is implemented. | Do not use order-payment semantics for auction deposits, holds, bids, or refunds. Agree on a separate contract before implementing an integration. |

WalletService owns wallet balances and wallet transactions. Other services must not read or write its database directly. Internal service traffic should use the private service network, not a public Gateway URL. Use the deployment's service DNS name and port `8008`; `localhost` is suitable only when the caller and WalletService run in the same network namespace.

## 2. Gateway integration

The Gateway is the frontend entry point and should proxy the supported public routes to WalletService without changing the business contract:

| Method | WalletService route |
|---|---|
| `GET` | `/api/v1/wallets/me` |
| `POST` | `/api/v1/wallets/topups/vnpay` |
| `POST` | `/api/v1/wallets/withdrawals` |
| `POST` | `/api/v1/wallets/transfers` |
| `GET` | `/api/v1/wallets/transactions` |
| `GET`, `POST` | `/api/v1/wallets/vnpay/ipn` (VNPay callback) |
| `GET` | `/api/v1/wallets/vnpay/return` |
| `POST` | `/api/v1/wallets/checkout/direct` |
| `GET` | `/api/v1/wallets/checkout/direct/{orderId}` |
| `PATCH` | `/api/v1/admin/wallets/withdrawals/{id}` (admin/staff) |
| `POST` | `/api/v1/admin/wallets/{walletId}/adjustments` (admin/staff) |
| `GET` | `/api/v1/admin/wallets/ledger` (admin only) |
| `GET` | `/api/v1/admin/wallets/reconciliation` (admin only) |


For authenticated requests, the gateway authenticates the caller and sets `x-user-id` (and `x-user-roles` for role-restricted routes), overwriting any client-supplied values. WalletService derives the wallet owner only from these headers and never from a request body.

Do not publish `POST /api/v1/wallets/orders/{orderId}/payment`, `POST /api/v1/wallets/sub-orders/{subOrderId}/settle`, or the order/sub-order `refund` routes as user-facing Gateway routes. Commerce calls them directly over the private service network. They are excluded from WalletService's public Swagger description; that exclusion is not network access control, and the settle and refund routes perform no identity check.

Pass through WalletService response status codes and bodies, including Problem Details errors. Do not convert a timeout into a success or a payment failure: the operation may have committed even if the Gateway did not receive its response.

## 3. User identity contract

WalletService does not validate JWTs, and the internal service-key middleware is disabled. It reads the caller from the forwarded `x-user-id` header and roles from `x-user-roles`. The service must be reachable only from the trusted gateway and private network; the gateway must overwrite client-supplied identity headers. The user ID must match the one used by Commerce orders.

The order-payment body contains only `amount`: Wallet derives the buyer from `x-user-id`. The settle and refund routes carry the seller or buyer ID in the body because they have no caller identity.

## 4. Commerce order-payment contract

### Request

Call after inventory has been reserved:

```http
POST /api/v1/wallets/orders/{orderId}/payment
x-user-id: <buyer-uuid>
Content-Type: application/json

{
  "amount": 480000
}
```

- `orderId` is the Commerce order's stable UUID and the payment idempotency key.
- `amount` is a positive decimal amount in VND, matching the order total Commerce intends to charge.
- Keep both values and the buyer identity unchanged across retries.

### Successful response

`200 OK` returns the wallet transaction:

```json
{
  "id": "transaction-id",
  "walletId": "wallet-id",
  "userId": "buyer-user-id",
  "type": "PurchaseDebit",
  "amount": 480000,
  "currency": "Vnd",
  "balanceAfter": 520000,
  "status": "Success",
  "note": "Payment for order via wallet",
  "createdAt": "2026-11-01T10:00:00+00:00"
}
```

### Processing sequence

1. Commerce reserves inventory using its synchronous Catalog integration.
2. Commerce calls WalletService with the order ID, amount, and buyer's `x-user-id`.
3. Wallet atomically debits the buyer's wallet and records the order ID as the payment reference.
4. On `200 OK`, Commerce marks the order paid and proceeds with checkout.
5. On a definitive Wallet business failure, Commerce releases the inventory reservation and returns the failure.

These are separate service-local database transactions, not one distributed transaction. Commerce owns the checkout orchestration and compensation; do not assume that a Wallet rollback can undo a Commerce or Catalog commit.

### Idempotency and failure handling

WalletService serializes concurrent attempts for the same order ID and persists the payment in PostgreSQL. Repeating the same order ID, buyer, and amount returns the original transaction without debiting again. Reusing the order ID with a different buyer or amount returns `409 PAYMENT_ORDER_CONFLICT`.

| Result | Meaning and caller action |
|---|---|
| `200 OK` | Payment succeeded or an identical retry returned the existing payment. Mark the order paid. |
| `400 Bad Request` | Invalid order ID or amount. Do not retry unchanged input. |
| `401 Unauthorized` | Missing or invalid `x-user-id`. Do not treat as a payment success. |
| `404 WALLET_NOT_FOUND` | No wallet exists for the buyer. This endpoint does not create one. Surface or handle as a definitive business failure. |
| `400 INSUFFICIENT_BALANCE` | Payment was not completed for insufficient funds. Release the inventory reservation according to Commerce's checkout policy. |
| `409 PAYMENT_ORDER_CONFLICT` | The order already has a payment with a different buyer or amount. Do not retry with altered details; reconcile the order and wallet transaction. |
| Timeout, connection loss, or `5xx` | Outcome may be unknown. Retry with the same order ID, buyer, and amount before compensation. Never create a new order ID merely to retry the same payment. |

Use bounded HTTP timeouts and a small, bounded retry policy for transient failures. Do not automatically retry ordinary `4xx` responses. Because a timeout does not establish whether Wallet committed, preserve the order's pending state until a same-key retry returns a definitive result; do not release inventory solely because the original response was lost.

## 5. HTTP client implementation guidance

Each caller should configure its WalletService base URL from deployment configuration, register a reusable HTTP client, set a bounded timeout, and propagate request cancellation. Send `x-user-id` only for a user-authorized operation. Avoid hard-coded `localhost` URLs in container deployments.

Example in .NET:

```csharp
services.AddHttpClient<WalletPaymentClient>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["Services:Wallet:BaseUrl"]
        ?? throw new InvalidOperationException("Services:Wallet:BaseUrl is required.");

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});
```

The timeout above is an example, not a WalletService SLA. Tune it with the deployment's request budget, and ensure the retry budget cannot exceed the caller's overall deadline. A client should deserialize and preserve WalletService's `errorCode` and HTTP status rather than interpreting every non-2xx response as the same error.

## 6. Future Auction integration

WalletService currently has no API to reserve/freeze wallet funds for auction deposits, bids, or refunds. The transfer, order-payment, settlement, and refund endpoints do not implement those semantics. Before AuctionService integrates:

1. Agree on a dedicated REST contract, ownership of the auction lifecycle, and response/error semantics.
2. Define a stable idempotency key for each auction operation and its retry behavior.
3. Specify how holds, capture, release, cancellation, and duplicate requests affect wallet balances and transactions.
4. Implement and document the WalletService endpoint before AuctionService depends on it.

Until that contract exists, AuctionService must not infer that a successful order payment is equivalent to an auction hold or deposit.

## 7. Operational notes

- WalletService listens on port `8008`; configure internal service discovery to reach it over the deployment's private network.
- This repository does not currently define a dedicated readiness endpoint. Do not use the root `GET /` response as proof that the database or a payment dependency is ready.
- No gRPC, message broker, fire-and-forget call, or payment event is part of the current service-to-service contract.
- The consumer contract is in [`docs/api/integration-contract.md`](../docs/api/integration-contract.md); route detail is in [`docs/api/wallet-payments.md`](../docs/api/wallet-payments.md). The broader REST and checkout behavior is in [`specs/02_API_REST_CONTRACTS.md`](02_API_REST_CONTRACTS.md) and [`specs/03_SYNCHRONOUS_REST_COMMUNICATION.md`](03_SYNCHRONOUS_REST_COMMUNICATION.md).
