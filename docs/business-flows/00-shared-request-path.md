# Shared request and response path

[Back to business use cases](../business-use-cases.md)

## Typical user request

```text
Client -> Gateway (when used) -> WalletService HTTP route
       -> identity/role filters -> controller -> MediatR validation
       -> command/query handler -> database and/or Stripe
       -> Wallet HTTP response -> Gateway response -> client
```

1. The checked-in Gateway's [example route configuration](../../Gateway/Gateway/appsettings.example.json)
   maps `/api/v1/wallets/{**catch-all}` and
   `/api/v1/admin/wallets/{**catch-all}` to the wallet cluster. Actual routing
   depends on deployed configuration. Gateway authentication, authorization,
   rate limits or other middleware may reject a request before Wallet sees it.
2. The Gateway's [identity transform](../../Gateway/Gateway/Transforms/IdentityPropagationTransform.cs)
   forwards available authenticated claims as headers. Wallet's
   [user context middleware](../../Wallet.Api/Middleware/UserContextMiddleware.cs)
   creates a principal from a valid `x-user-id` and optional role headers.
   Wallet's general internal-service authentication middleware is disabled;
   auction hold service actions separately require their scoped key. The
   Wallet host does not itself validate a bearer token in this pipeline.
3. [WalletsController](../../Wallet.Api/Controllers/Public/WalletsController.cs)
   or [AdminWalletsController](../../Wallet.Api/Controllers/Public/AdminWalletsController.cs)
   takes route/body data, applies relevant filters, supplies server-derived
   identity where needed, and sends a command or query. The
   [validation behavior](../../Wallet.Application/Common/Behaviors/ValidationBehavior.cs)
   validates MediatR requests before the handler.
4. Handlers use `IWalletDbContext`, wallet domain operations and, for Stripe,
   provider interfaces. Mutation paths described below use transactions and
   locks where implemented. A response is returned only after the relevant
   commit or provider interaction succeeds.

Auction and Commerce service calls may reach Wallet directly instead. Stripe
webhooks are provider calls, not ordinary authenticated user requests. A
background worker has no incoming Gateway request at all.

## Response boundary

Wallet returns unwrapped JSON DTOs on successful controller actions, or an
empty `200` for an acknowledged Stripe webhook. Lists use `items` and
`pagination` (`pageIndex`, `pageSize`, `totalCount`, `totalPages`,
`hasPreviousPage`, `hasNextPage`).

When a response goes through the checked-in Gateway, its
[response middleware](../../Gateway/Gateway/Middlewares/Global/ResponseEnvelopeMiddleware.cs)
wraps nonempty successful JSON not already enveloped as
`{ success, statusCode, message, data, traceId, timestamp }`. It passes through
non-success responses, empty bodies, redirects and non-JSON responses. Thus
the Wallet DTO becomes `data` for the usual Gateway success response; a direct
service caller receives the DTO itself. Gateway configuration and earlier
middleware can change the observed end-to-end result.

Wallet's [exception middleware](../../Wallet.Api/Middleware/GlobalExceptionHandlerMiddleware.cs)
maps failures to `application/problem+json` with status, error code, detail
and trace ID; validation may include field errors. Authentication and feature
filters can also return Problem Details. Typical outcomes include `400` for
invalid input or insufficient funds, `401`/`403` for identity or role errors,
`404` for missing resources, `409` for conflicts, and `503` for disabled
features or unavailable providers. Exact outcomes depend on the operation.

## Common safety concepts

- Wallet operations that debit or reserve money use available, not merely
  total, balance; other reservations remain protected.
- A retry is only safe where an operation explicitly implements idempotency.
  Do not assume all POSTs have this guarantee.
- Status returned immediately from a payment initiation can be `Pending`;
  a browser redirect is not financial confirmation.
