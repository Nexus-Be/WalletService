# Wallet-to-Wallet Transfers — Sequence Diagram

[Detailed walkthrough](05-transfers.md) | [Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

Atomic P2P VND wallet transfer with deadlock-free ascending `UserId` locking, optional `Idempotency-Key` SHA-256 verification, strict `Active` & `AvailableBalance` enforcement, and symmetric `TRANSFER_CLEARING` shadow-ledger capture.

```mermaid
sequenceDiagram
    actor Sender as Wallet Owner (Sender)
    participant API as WalletsController / TransferCommandHandler
    participant DB as PostgreSQL (Wallets / Transactions / AuditLogs)

    Note over Sender,DB: == 1. Request & Validation ==
    Sender->>API: POST /api/v1/wallets/transfers (recipientUserId, amount 1k..100M VND, note?, Idempotency-Key?)
    API->>API: Controller injects SenderId from x-user-id & IdempotencyKey header
    API->>API: ValidationBehavior checks non-self recipient, whole/precision bounds, key format
    alt Validation Fails (e.g. Self-Transfer or Out of Range)
        API-->>Sender: 400 VAL_INVALID_INPUT
    else Valid Input
        Note over Sender,DB: == 2. Deadlock-Free Sorted Locking & Idempotency ==
        API->>DB: BeginTx -> LockWalletByUserIdAsync(min(SenderId, RecipientId)) then max(SenderId, RecipientId)
        alt Missing Sender or Recipient Wallet (No Auto-Create)
            DB-->>API: null wallet
            API-->>Sender: 404 WALLET_NOT_FOUND
        else Both Wallets Locked
            opt Idempotency-Key Header Provided
                API->>API: Compute SHA-256(SenderGuid, RecipientGuid, Amount, TrimmedNote)
                API->>DB: Query WalletTransactions(SenderWalletId, TransferOut, ClientIdempotencyKey)
                alt Matching Fingerprint Found
                    DB-->>API: Prior TransferOut transaction
                    API-->>Sender: 200 Replayed TransferResponse (no double debit)
                else Key Exists With Different Payload
                    DB-->>API: Mismatched fingerprint
                    API-->>Sender: 409 IDEMPOTENCY_KEY_REUSED
                end
            end

            Note over Sender,DB: == 3. Eligibility Checks & Atomic Transfer ==
            API->>DB: Enforce EnsureActive() on BOTH Sender and Recipient + Currency match
            alt Either Wallet Frozen
                API-->>Sender: 403 WALLET_FROZEN
            else Currency Mismatch or Sender AvailableBalance < Amount
                API-->>Sender: 400 Currency Error or INSUFFICIENT_BALANCE
            else Eligible
                DB->>DB: senderWallet.Deduct(amount) + recipientWallet.Deposit(amount)
                DB->>DB: Insert TransferOut & TransferIn (shared ReferenceId = transferId, same CreatedAt)
                DB->>DB: Insert AuditAction.Transfer & SaveChangesAsync (emits TRANSFER_CLEARING events if Shadow Ledger on)
                DB-->>API: Commit
                API-->>Sender: 200 TransferResponse (transactionId, amount, currency, status Success, createdAt)
            end
        end
    end
```
