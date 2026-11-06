# 03 — SYNCHRONOUS REST COMMUNICATION

All inter-service business communication uses synchronous REST over HTTP/JSON. A caller waits for the callee's response and proceeds based on the returned result. This describes the transport contract, not blocking application code: .NET endpoints and database operations should remain asynchronous I/O using `async`/`await`.

## Checkout payment flow

1. Commerce reserves inventory through a synchronous Catalog REST request.
2. Commerce calls `POST /api/v1/wallets/orders/{orderId}/payment` with the buyer's bearer token and order amount.
3. Wallet atomically debits the wallet and writes a `PurchaseDebit` transaction, then returns the transaction response.
4. Commerce marks the order paid only after a successful Wallet response.
5. On a Wallet business failure, Commerce releases the inventory reservation through a synchronous Catalog REST request and returns the failure to its caller.

No `StockReservedEvent`, `PaymentCompletedEvent`, or `PaymentFailedEvent` is published or consumed. No broker, asynchronous consumer, or transactional message outbox is used.

## Retry and failure behavior

- `orderId` is the payment idempotency key and must remain stable across retries.
- Wallet serializes concurrent attempts for the same order ID with a PostgreSQL transaction advisory lock and stores the ID on `WalletTransaction.ReferenceId`.
- An identical retry returns the original payment transaction. A request reusing that order ID with a different buyer or amount returns `409 ORDER_ALREADY_PAID`.
- A timeout is an unknown outcome, not proof of failure. Commerce should retry the same request with the same order ID before initiating compensation.
- REST calls do not create a distributed ACID transaction. Each service commits its own database operation; Commerce owns orchestration and compensation.

## Transport constraints

- Do not add gRPC, RabbitMQ, MassTransit, fire-and-forget messaging, or event-based service communication.
- Require authenticated bearer identity for wallet operations and forward the buyer's token when an operation is performed on that buyer's behalf.
- Preserve request cancellation and use bounded HTTP timeouts/retries in service callers. Retries for financial operations must use a stable idempotency key.
