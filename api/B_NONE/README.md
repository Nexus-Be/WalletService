# B_NONE — APIs not complete or not verified in this repository

This directory contains API specifications that are not implemented here, or cannot be verified in this repository. It describes **this WalletService repository only**; an API listed below may be implemented in another service or repository. These specifications document the expected ecosystem behavior and are not proof of implementation.

## WalletService — incomplete capability

| API / capability | Current state | Remaining work |
|---|---|---|
| `POST /api/v1/wallets/withdrawals` — bank withdrawal | **Partial** | WalletService creates a pending request and reserves available funds; staff can approve/reject it. No bank payout, seller-verification check, payout retry, or reconciliation is implemented. |
| [Wallet and payments ecosystem specification](./09_WALLET_PAYMENTS_API.md) | **Not implemented/verified in this repository** | Only the [implemented WalletService routes](../../docs/api/wallet-payments.md) have been verified against source in this repository. |

The route currently accepts:

```http
POST /api/v1/wallets/withdrawals
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "amount": 50000,
  "currency": "VND",
  "bankCode": "BANK",
  "accountNumber": "123456789",
  "accountHolderName": "NGUYEN VAN A"
}
```

Do not treat its `201 Created` response or a staff approval as evidence that funds were paid to a bank account.

## APIs owned by services outside this repository

No implementation source for these services is present in this repository, so their runtime status cannot be verified here:

| Module | Service | Ecosystem specification |
|---|---|---|
| Authentication | `UserService` | [`01_AUTH_API.md`](./01_AUTH_API.md) |
| User profile | `UserService` | [`02_USER_PROFILE_API.md`](./02_USER_PROFILE_API.md) |
| Admin users | `UserService` | [`03_ADMIN_USERS_API.md`](./03_ADMIN_USERS_API.md) |
| Products and catalog | `CatalogService` | [`04_PRODUCTS_CATALOG_API.md`](./04_PRODUCTS_CATALOG_API.md) |
| Cart and commerce | `CommerceService` | [`05_CART_COMMERCE_API.md`](./05_CART_COMMERCE_API.md) |
| Orders and checkout | `CommerceService` | [`06_ORDERS_CHECKOUT_API.md`](./06_ORDERS_CHECKOUT_API.md) |
| Auctions | `AuctionService` | [`07_AUCTIONS_API.md`](./07_AUCTIONS_API.md) |
| Fulfillment and shipping | `FulfillmentService` | [`08_FULFILLMENT_SHIPPING_API.md`](./08_FULFILLMENT_SHIPPING_API.md) |
| Notifications | `NotificationService` | [`10_NOTIFICATIONS_API.md`](./10_NOTIFICATIONS_API.md) |
| Gateway control and health | `ApiGateway` | [`11_GATEWAY_CONTROL_AND_HEALTH_API.md`](./11_GATEWAY_CONTROL_AND_HEALTH_API.md) |
