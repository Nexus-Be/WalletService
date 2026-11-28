START TRANSACTION;


-- Generated from the two Stripe EF migrations; apply only after backup and review.
-- The baseline check must run before either migration changes the database.
DO $preflight$
BEGIN
    IF to_regclass('"__EFMigrationsHistory"') IS NULL THEN
        RAISE EXCEPTION 'Missing EF migration history. Do not apply this script to an unverified database.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031002425_InitialCreate')
       OR NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103035428_AddDirectPaymentOrderIdempotency') THEN
        RAISE EXCEPTION 'Missing required WalletService baseline migrations. Stop and verify the database schema and history.';
    END IF;
    IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112023501_AllowStripePaymentParents')
       AND NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
        RAISE EXCEPTION 'Inconsistent Stripe migration history. Stop and investigate before applying this script.';
    END IF;
END $preflight$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    CREATE TABLE "StripePaymentAttempts" (
        "Id" uuid NOT NULL,
        "Purpose" integer NOT NULL,
        "OwnerId" uuid NOT NULL,
        "WalletTopupId" uuid,
        "DirectPaymentOrderId" uuid,
        "Amount" numeric(18,2) NOT NULL,
        "RequestFingerprint" character varying(64) NOT NULL,
        "ProviderIdempotencyKey" character varying(255) NOT NULL,
        "ClientIdempotencyKey" character varying(128),
        "CheckoutSessionId" character varying(255),
        "PaymentIntentId" character varying(255),
        "CheckoutUrl" character varying(2048),
        "SessionExpiresAt" timestamp with time zone,
        "Status" integer NOT NULL,
        "BlocksReplacement" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        CONSTRAINT "PK_StripePaymentAttempts" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_StripePaymentAttempts_Purpose" CHECK (("Purpose" = 0 AND "WalletTopupId" IS NOT NULL AND "DirectPaymentOrderId" IS NULL) OR ("Purpose" = 1 AND "WalletTopupId" IS NULL AND "DirectPaymentOrderId" IS NOT NULL)),
        CONSTRAINT "CK_StripePaymentAttempts_WholeAmount" CHECK ("Amount" > 0 AND "Amount" = trunc("Amount")),
        CONSTRAINT "FK_StripePaymentAttempts_DirectPaymentOrders_DirectPaymentOrde~" FOREIGN KEY ("DirectPaymentOrderId") REFERENCES "DirectPaymentOrders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_StripePaymentAttempts_WalletTopups_WalletTopupId" FOREIGN KEY ("WalletTopupId") REFERENCES "WalletTopups" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    CREATE TABLE "StripeWebhookEvents" (
        "Id" uuid NOT NULL,
        "ProviderEventId" character varying(255) NOT NULL,
        "EventType" character varying(128) NOT NULL,
        "PaymentAttemptId" uuid,
        "Outcome" character varying(128) NOT NULL,
        "ReceivedAt" timestamp with time zone NOT NULL,
        "ProcessedAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        CONSTRAINT "PK_StripeWebhookEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_StripeWebhookEvents_StripePaymentAttempts_PaymentAttemptId" FOREIGN KEY ("PaymentAttemptId") REFERENCES "StripePaymentAttempts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    CREATE UNIQUE INDEX "IX_StripePaymentAttempts_CheckoutSessionId" ON "StripePaymentAttempts" ("CheckoutSessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    CREATE UNIQUE INDEX "IX_StripePaymentAttempts_DirectPaymentOrderId" ON "StripePaymentAttempts" ("DirectPaymentOrderId") WHERE "BlocksReplacement" = TRUE AND "DirectPaymentOrderId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    CREATE UNIQUE INDEX "IX_StripePaymentAttempts_OwnerId_ClientIdempotencyKey" ON "StripePaymentAttempts" ("OwnerId", "ClientIdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    CREATE UNIQUE INDEX "IX_StripePaymentAttempts_PaymentIntentId" ON "StripePaymentAttempts" ("PaymentIntentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    CREATE UNIQUE INDEX "IX_StripePaymentAttempts_ProviderIdempotencyKey" ON "StripePaymentAttempts" ("ProviderIdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    CREATE UNIQUE INDEX "IX_StripePaymentAttempts_WalletTopupId" ON "StripePaymentAttempts" ("WalletTopupId") WHERE "BlocksReplacement" = TRUE AND "WalletTopupId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    CREATE INDEX "IX_StripeWebhookEvents_PaymentAttemptId" ON "StripeWebhookEvents" ("PaymentAttemptId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    CREATE UNIQUE INDEX "IX_StripeWebhookEvents_ProviderEventId" ON "StripeWebhookEvents" ("ProviderEventId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112021318_AddStripePaymentFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261112021318_AddStripePaymentFoundation', '8.0.10');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112023501_AllowStripePaymentParents') THEN
    ALTER TABLE "WalletTopups" ALTER COLUMN "VnpTxnRef" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112023501_AllowStripePaymentParents') THEN
    ALTER TABLE "DirectPaymentOrders" ALTER COLUMN "VnpTxnRef" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112023501_AllowStripePaymentParents') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261112023501_AllowStripePaymentParents', '8.0.10');
    END IF;
END $EF$;
COMMIT;
