# 06 — Synchronous Service Integration Guide

This guide is for the Gateway, User, Commerce, and future Auction repositories integrating with WalletService. It describes the current HTTP contracts and separates implemented behavior from integration guidance. WalletService uses synchronous REST over HTTP/JSON; callers wait for a response, while server-side I/O remains asynchronous.

## 1. Service boundaries and current integration status

| Service | Current relationship to WalletService | Integration responsibility |
|---|---|---|
| API Gateway | Wallet routes are implemented, but Gateway routing is not verified in this repository. | Proxy the supported client-facing Wallet routes and preserve the caller's bearer token, request, status code, and response. Do not expose the internal order-payment route as a frontend operation. |
| UserService | Wallet validates JWTs using its configured HTTPS issuer and reads the user ID from the configured claim (default: `sub`). There is no synchronous UserService lookup in Wallet request handling. | Issue tokens with a stable user-ID claim and configure the same issuer, audience, and claim contract in WalletService. A request to UserService is not needed to authorize each Wallet call. |
| CommerceService | The authenticated internal order-payment route is implemented. | Call Wallet after reserving inventory; only mark the order paid after a successful Wallet response. On a confirmed business failure, release the reservation. |
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
| `GET` | `/api/v1/wallet/vnpay/ipn` and `/api/v1/wallets/vnpay/ipn` |
| `GET` | `/api/v1/wallet/vnpay/return` and `/api/v1/wallets/vnpay/return` |

For authenticated requests, forward the original `Authorization: Bearer <user-access-token>` header. WalletService validates the token itself and derives the wallet owner from its authenticated claims; do not replace the identity with a user ID supplied by the frontend or trust identity headers as a substitute for the token.

Do not publish `POST /api/v1/internal/wallet/orders/{orderId}/payment` as a user-facing Gateway route. Commerce calls it directly over the private service network. The route is excluded from WalletService's public Swagger description; that exclusion alone is not network access control.

Pass through WalletService response status codes and bodies, including Problem Details errors. Do not convert a timeout into a success or a payment failure: the operation may have committed even if the Gateway did not receive its response.

## 3. User identity and token contract

WalletService does not call UserService synchronously to look up a user during each request. It validates JWT issuer, audience, signature, and lifetime locally using the configured HTTPS authority, then reads the configured user-ID claim (`sub` by default). The claim must identify the same user ID used by Commerce orders.

Configure WalletService with:

- `Authentication:Authority`: the HTTPS token issuer URL.
- `Authentication:Audience`: the audience expected by WalletService.
- `Authentication:UserIdClaimType`: the stable user-ID claim name, default `sub`.

The Gateway and Commerce must forward the user's bearer token when performing operations on that user's behalf. Do not send a user ID in the order-payment body: the current request body contains only `amount`, and Wallet derives the buyer from the token. If future service-to-service authentication needs a distinct service identity, define and implement that contract explicitly; the current endpoint requires a user identity.

## 4. Commerce order-payment contract

### Request

Call after inventory has been reserved:

```http
POST /api/v1/internal/wallet/orders/{orderId}/payment
Authorization: Bearer <buyer's access token>
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
  "note": "Order payment #6f3a5a51-a35f-4e56-a960-00ab7d54e9fd",
  "createdAt": "2026-11-01T10:00:00+00:00"
}
```

### Processing sequence

1. Commerce reserves inventory using its synchronous Catalog integration.
2. Commerce calls WalletService with the order ID, amount, and buyer's bearer token.
3. Wallet atomically debits the buyer's wallet and records the order ID as the payment reference.
4. On `200 OK`, Commerce marks the order paid and proceeds with checkout.
5. On a definitive Wallet business failure, Commerce releases the inventory reservation and returns the failure.

These are separate service-local database transactions, not one distributed transaction. Commerce owns the checkout orchestration and compensation; do not assume that a Wallet rollback can undo a Commerce or Catalog commit.

### Idempotency and failure handling

WalletService serializes concurrent attempts for the same order ID and persists the payment in PostgreSQL. Repeating the same order ID, buyer, and amount returns the original transaction without debiting again. Reusing the order ID with a different buyer or amount returns `409 ORDER_ALREADY_PAID`.

| Result | Meaning and caller action |
|---|---|
| `200 OK` | Payment succeeded or an identical retry returned the existing payment. Mark the order paid. |
| `400 Bad Request` | Invalid order ID or amount. Do not retry unchanged input. |
| `401 Unauthorized` | Missing, invalid, expired, or unusable buyer identity. Do not treat as a payment success. |
| `404 WALLET_NOT_FOUND` | No wallet exists for the buyer. This endpoint does not create one. Surface or handle as a definitive business failure. |
| `409 INSUFFICIENT_BALANCE` | Payment was not completed for insufficient funds. Release the inventory reservation according to Commerce's checkout policy. |
| `409 ORDER_ALREADY_PAID` | The order reference has already been used with different payment details. Do not retry with altered details; reconcile the order and wallet transaction. |
| Timeout, connection loss, or `5xx` | Outcome may be unknown. Retry with the same order ID, buyer token, and amount before compensation. Never create a new order ID merely to retry the same payment. |

Use bounded HTTP timeouts and a small, bounded retry policy for transient failures. Do not automatically retry ordinary `4xx` responses. Because a timeout does not establish whether Wallet committed, preserve the order's pending state until a same-key retry returns a definitive result; do not release inventory solely because the original response was lost.

## 5. HTTP client implementation guidance

Each caller should configure its WalletService base URL from deployment configuration, register a reusable HTTP client, set a bounded timeout, and propagate request cancellation. Forward the buyer's token only for a user-authorized operation. Avoid hard-coded `localhost` URLs in container deployments.

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

WalletService currently has no API to reserve/freeze wallet funds for auction deposits, bids, or refunds. The transfer and order-payment endpoints do not implement those semantics. Before AuctionService integrates:

1. Agree on a dedicated REST contract, ownership of the auction lifecycle, and response/error semantics.
2. Define a stable idempotency key for each auction operation and its retry behavior.
3. Specify how holds, capture, release, cancellation, and duplicate requests affect wallet balances and transactions.
4. Implement and document the WalletService endpoint before AuctionService depends on it.

Until that contract exists, AuctionService must not infer that a successful order payment is equivalent to an auction hold or deposit.

## 7. Operational notes

- WalletService listens on port `8008`; configure internal service discovery to reach it over the deployment's private network.
- This repository does not currently define a dedicated readiness endpoint. Do not use the root `GET /` response as proof that the database or a payment dependency is ready.
- No gRPC, message broker, fire-and-forget call, or payment event is part of the current service-to-service contract.
- The verified WalletService API details are in [`docs/api/wallet-payments.md`](../docs/api/wallet-payments.md). The broader REST and checkout behavior is in [`specs/02_API_REST_CONTRACTS.md`](02_API_REST_CONTRACTS.md) and [`specs/03_SYNCHRONOUS_REST_COMMUNICATION.md`](03_SYNCHRONOUS_REST_COMMUNICATION.md).
