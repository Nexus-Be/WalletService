# Stripe migration validation quickstart

1. Use a disposable PostgreSQL database and a Stripe test-mode account. Confirm
   the account supports VND and the desired test payment methods. Never put
   API keys or webhook secrets in tracked files.
2. Configure the Stripe test secret key, webhook signing secret, and allowed
   frontend success/cancel URLs through environment variables. Register a
   webhook endpoint or use the Stripe CLI to forward test events.
3. Start WalletService and the gateway using the existing local deployment
   instructions. Confirm raw webhook payloads and `Stripe-Signature` survive
   gateway forwarding.
4. Create a top-up and follow the hosted URL. Confirm a successful provider
   event credits the wallet once; replay the event and verify no extra credit.
5. Test an asynchronous method, an expired or failed attempt, a direct order
   retry after provider confirmation, and a cross-user status request.
6. Run the architecture verifier, Release build and all tests specified in
   [plan.md](plan.md). Inspect logs for explicit errors and no secret values.

The WalletService routes are wired and locally tested, but a real Stripe
test-mode payment and gateway forwarding have not been verified.

The provider client currently reads `Stripe__SecretKey`, `Stripe__SuccessUrl`,
and `Stripe__CancelUrl` from environment configuration. Use an `sk_test_`
secret key and absolute HTTPS redirect URLs. The webhook verifier
uses `Stripe__WebhookSecret`, a signing secret beginning with `whsec_`.
Configure the Stripe endpoint to use the
API version expected by the pinned Stripe.net SDK. The SDK client
uses its pinned API version, a ten-second HTTP timeout, and no automatic
network retries; the caller must retain and reuse the persisted idempotency
key. Never treat a timeout as proof that Stripe did not create a session.
The provider client can retrieve a Checkout Session and PaymentIntent.
Application handlers and HTTP routes prepare attempts and process verified
provider snapshots. Local tests do not establish end-to-end Stripe or gateway
behavior.

The PostgreSQL integration tests migrate and write to the configured database;
use an isolated disposable database only:

```bash
export WALLET_STRIPE_TEST_DATABASE='<disposable PostgreSQL connection string>'
dotnet test Wallet.Tests/Wallet.Tests.csproj -c Release --filter FullyQualifiedName~StripeSettlementPostgresTests
```

Without that variable the PostgreSQL tests are explicitly skipped. Never
configure a shared or production database for these tests.

## Configure Stripe and migrate an existing Neon database

The ignored root `.env` is used by Docker Compose. Set `STRIPE_SECRET_KEY`
to a Stripe test secret key (`sk_test_...`), `STRIPE_WEBHOOK_SECRET` to the
signing secret (`whsec_...`) for the registered test webhook or Stripe CLI
forwarder, and `STRIPE_SUCCESS_URL` / `STRIPE_CANCEL_URL` to the actual
absolute HTTPS frontend URLs. Never commit or share the secrets. Compose maps
these settings to `Stripe__SecretKey`, `Stripe__WebhookSecret`,
`Stripe__SuccessUrl`, and `Stripe__CancelUrl`; use those double-underscore
names when running the application directly without Compose. Blank values
are placeholders, not a working Stripe configuration.

For local webhook testing, install and authenticate the Stripe CLI, then
forward test events directly to WalletService:

```bash
stripe login
stripe listen --events checkout.session.completed,checkout.session.async_payment_succeeded,checkout.session.async_payment_failed,checkout.session.expired --forward-to http://localhost:8008/api/v1/wallets/stripe/webhooks
```

Keep the listener running. Copy the signing secret displayed by that listener
into the ignored `.env` as `STRIPE_WEBHOOK_SECRET` without sharing it or
committing it, then restart WalletService so it reads the new value. The CLI
listener's secret may differ from a Dashboard endpoint's secret; use the one
for the active listener. This direct forwarding tests WalletService, not
gateway forwarding. If `WALLET_SERVICE_PORT` is not 8008, use its configured
host port instead.

The local frontend currently starts over HTTP. Stripe redirect configuration
requires an absolute HTTPS frontend origin, and the proposed result page
needs frontend work; see the
[frontend handover](../../docs/api/stripe/frontend-gateway-handover.md).
Do not substitute an invented URL or treat the Stripe redirect as proof of
payment.

The application automatically applies EF migrations during startup. Before
starting the updated service against Neon, coordinate a maintenance window,
stop all application instances and other migration runners, and make and
verify a Neon backup or restore point. Confirm that the selected Neon branch
and database are the intended target. Do not run PostgreSQL integration tests
against Neon.

1. In the Neon SQL editor, inspect the existing migration history:

   ```sql
   SELECT "MigrationId", "ProductVersion"
   FROM "__EFMigrationsHistory"
   ORDER BY "MigrationId";
   ```

   The database must already contain both
   `20261031002425_InitialCreate` and
   `20261103035428_AddDirectPaymentOrderIdempotency`, and its schema must
   match that history. If the history table or either baseline migration is
   missing, or the database was changed manually, stop and investigate;
   never add history rows merely to bypass this check.
2. Review and run the complete
   [Neon Stripe migration script](../../scripts/neon-stripe-migration.sql)
   in the Neon SQL editor. It applies only the two Stripe EF migrations and
   checks the baseline before making changes. Both migrations run together
   in one transaction. The script can be rerun after resolving an error.
   It does not remove existing VNPay records or columns.
3. Rerun the history query and confirm
   `20261112021318_AddStripePaymentFoundation` and
   `20261112023501_AllowStripePaymentParents` are present. Verify that
   `StripePaymentAttempts` and `StripeWebhookEvents` exist and the legacy
   `VnpTxnRef` columns on `WalletTopups` and `DirectPaymentOrders` are
   nullable. Resume the service only after the migration is verified.

An idempotent migration history check is not a substitute for a verified
backup or for checking schema drift. These Stripe migrations do not support
automatic rollback; use an approved restore/recovery plan if needed.

## Live release gates

- [ ] Confirm the actual Stripe account's eligibility, VND presentment,
      payment methods, relevant amount limits and settlement configuration.
- [ ] Obtain Stripe approval for the specific transferable-wallet funding
      model and document any conditions.
- [ ] Obtain an approved operational policy for refunds, disputes, reversals
      and extra charges; identify the owners responsible for manual action.

Test-mode implementation does not satisfy these gates. Do not enable live
payments until they are explicitly confirmed.
