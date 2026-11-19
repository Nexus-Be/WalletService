# WalletService — POST ~/api/v1/internal/wallets/orders/{orderId:guid}/escrow/cancel

> **Feature / Use Case:** `CancelOrderEscrow`
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-19
> **Tác giả:** Antigravity AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `~/api/v1/internal/wallets/orders/{orderId:guid}/escrow/cancel`
- **Quyền hạn (RBAC Policy):** Internal Service-to-Service (`[RequireInternalServiceKey]` checking `X-Internal-Service-Key`; `[ApiExplorerSettings(IgnoreApi = true)]`; NOT gated by `ReleaseFeature.LegacyCommerceCredits` because the operation is strictly funding-backed by `OrderEscrow`)
- **Quy tắc Sở hữu (Resource Ownership):** Destination wallet is always resolved strictly from `OrderEscrow.WalletId` (the wallet that funded the order payment). Optional `expectedBuyerId` acts solely as an ownership verification assertion and rejects mismatches with `409 ORDER_ESCROW_OWNERSHIP_CONFLICT`.
- **Controller:** `Wallet.Api/Controllers/Public/WalletsController.cs` (`CancelOrderEscrowInternal`)
- **CQRS Handler:** `Wallet.Application/Features/Checkout/Commands/CancelOrderEscrow/CancelOrderEscrowCommandHandler.cs` (Controller injects `ISender` only)

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Request Headers
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `orderId` | Route | `Guid` | Có | Non-empty UUID | Khóa đơn hàng tương ứng `OrderEscrow.OrderId` |
| `X-Internal-Service-Key` | Header | `string` | Có | Matches `INTERNAL_SERVICE_SECRET` / `InternalSecurity:ServiceKey` | Xác thực S2S cho CommerceService |
| `Idempotency-Key` | Header | `string` | Có | `1..100` ASCII letters, digits, `-`, or `_` | Bắt buộc chống lặp hoặc xung đột payload |

### 2.2 Request Body / Command Record
- **Vị trí file:** `Wallet.Application/Features/Checkout/Commands/CancelOrderEscrow/CancelOrderEscrowCommand.cs`
- **Tên C# Record:** `CancelOrderEscrowCommand`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record CancelOrderEscrowCommand(
    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Reason must be between 3 and 500 characters.")]
    string Reason,
    string? ExpectedBuyerId = null,
    decimal? ExpectedAmount = null) : IRequest<CancelOrderEscrowResponse>, IRequestValidation
{
    [JsonIgnore] public Guid OrderId { get; init; }

    [JsonIgnore] public string IdempotencyKey { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var orderError = RequestValidation.ValidateGuid(OrderId, nameof(OrderId), "Order ID");
        if (orderError is not null)
        {
            yield return orderError;
        }

        if (!WithdrawalWorkflow.IsValidIdempotencyKey(IdempotencyKey))
        {
            yield return new ValidationResult(
                "Idempotency-Key header is required and must be 1 to 100 ASCII letters, digits, hyphens, or underscores.",
                new[] { nameof(IdempotencyKey) });
        }

        if (ExpectedBuyerId is not null)
        {
            var buyerError = RequestValidation.ValidateGuid(
                ExpectedBuyerId,
                nameof(ExpectedBuyerId),
                "Expected buyer ID");
            if (buyerError is not null)
            {
                yield return buyerError;
            }
        }

        if (ExpectedAmount is decimal expectedAmount)
        {
            if (expectedAmount <= 0m)
            {
                yield return new ValidationResult(
                    "Expected amount must be greater than zero.",
                    new[] { nameof(ExpectedAmount) });
            }
            else if (expectedAmount > 9999999999999999.99m ||
                     expectedAmount != decimal.Round(expectedAmount, 2))
            {
                yield return new ValidationResult(
                    "Expected amount must fit the supported decimal(18,2) range.",
                    new[] { nameof(ExpectedAmount) });
            }
        }
    }
}
```
- **JSON Request Body mẫu (camelCase):**
```json
{
  "reason": "Order cancelled before fulfillment",
  "expectedBuyerId": "11111111-1111-1111-1111-111111111111",
  "expectedAmount": 220000.00
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** WalletService CHỈ trả về DTO thuần, KHÔNG bọc qua `ApiResponse<T>`. Gateway không expose `/api/v1/internal/*`.

- **Vị trí file:** `Wallet.Application/Features/Checkout/Commands/CancelOrderEscrow/CancelOrderEscrowCommand.cs`
- **Tên C# Record:** `CancelOrderEscrowResponse`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record CancelOrderEscrowResponse(
    string TransactionId,
    string OrderId,
    string WalletId,
    string UserId,
    string Type,
    decimal ReturnedAmount,
    string Currency,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string EscrowStatus,
    string Status,
    string Note,
    DateTimeOffset CancelledAt,
    bool IsReplay);
```
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "transactionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderId": "22222222-2222-2222-2222-222222222222",
  "walletId": "33333333-3333-3333-3333-333333333333",
  "userId": "11111111-1111-1111-1111-111111111111",
  "type": "EscrowCancellationRefund",
  "returnedAmount": 220000.00,
  "currency": "Vnd",
  "balanceBefore": 280000.00,
  "balanceAfter": 500000.00,
  "escrowStatus": "Cancelled",
  "status": "Success",
  "note": "Order escrow cancelled: Order cancelled before fulfillment",
  "cancelledAt": "2026-11-19T10:15:00+00:00",
  "isReplay": false
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)
> **Quy chuẩn:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (không dùng tiền tố `ERR_`).

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `One or more validation errors occurred.` | Vi phạm DataAnnotations hoặc `IRequestValidation` qua `ValidationBehavior` |
| `400 Bad Request` | `VAL_INVALID_ORDER_ID` | `Order ID must be a valid UUID.` | `OrderId == Guid.Empty` khi gọi handler trực tiếp |
| `400 Bad Request` | `VAL_INVALID_IDEMPOTENCY_KEY` | `Idempotency-Key header is required and must be 1 to 100 ASCII letters, digits, hyphens, or underscores.` | `IdempotencyKey` rỗng hoặc sai format khi gọi handler trực tiếp |
| `400 Bad Request` | `VAL_INVALID_BUYER_ID` | `Expected buyer ID must be a valid UUID.` | `ExpectedBuyerId` không hợp lệ khi gọi handler trực tiếp |
| `400 Bad Request` | `VAL_INVALID_AMOUNT` | `Expected amount must be a positive decimal with at most two fractional digits.` | `ExpectedAmount` âm, bằng 0 hoặc quá 2 chữ số thập phân khi gọi handler trực tiếp |
| `401 Unauthorized` | `UNAUTHORIZED` | `Internal service authentication is required.` | Thiếu hoặc sai header `X-Internal-Service-Key` |
| `404 Not Found` | `ORDER_ESCROW_NOT_FOUND` | `No wallet-payment escrow was found for the order.` | Không tìm thấy `PurchaseDebit` / `OrderEscrow` cho `orderId` |
| `409 Conflict` | `IDEMPOTENCY_KEY_REUSED` | `The idempotency key was already used for a different order operation.` | Trùng `Idempotency-Key` trên `ReferenceId` nhưng khác operation hoặc payload fingerprint |
| `409 Conflict` | `ORDER_ESCROW_ALREADY_CANCELLED` | `The order escrow was already cancelled.` | `OrderEscrow.Status == Cancelled` nhưng gọi với `Idempotency-Key` mới |
| `409 Conflict` | `ORDER_ESCROW_ALREADY_RESOLVED` | `The order escrow was already settled or refunded and cannot be cancelled.` | `OrderEscrow.Status` là `Settled`/`Refunded` hoặc `RemainingAmount == 0` |
| `409 Conflict` | `ORDER_ESCROW_PARTIALLY_RESOLVED` | `The order escrow has already been partially settled or refunded and cannot be cancelled in full.` | Chế độ all-or-nothing phát hiện `RemainingAmount != OriginalAmount` |
| `409 Conflict` | `ORDER_ESCROW_UNAVAILABLE` | `The order's wallet-payment escrow cannot be safely cancelled.` | Lịch sử giao dịch cũ không thể đối soát an toàn thành 1 escrow duy nhất |
| `409 Conflict` | `ORDER_ESCROW_OWNERSHIP_CONFLICT` | `The expected buyer does not match the wallet that funded the order escrow.` | `ExpectedBuyerId` khác `wallet.UserId` của `OrderEscrow.WalletId` |
| `409 Conflict` | `ORDER_ESCROW_AMOUNT_MISMATCH` | `The expected amount does not match the remaining order escrow balance.` | `ExpectedAmount` khác `escrow.RemainingAmount` |
| `500 Internal Error` | `INTERNAL_CONFIGURATION_ERROR` | `Internal service authentication is not configured.` | Chưa cấu hình `INTERNAL_SERVICE_SECRET` / `InternalSecurity:ServiceKey` |

### 4.1 Bảng Ánh xạ Enum liên quan
| Tên Enum C# | Giá trị DB (Integer) | Giá trị API (PascalCase) | Ý nghĩa nghiệp vụ |
| :--- | :---: | :--- | :--- |
| `OrderEscrowStatus.Active` | `0` | `"Active"` | Escrow còn số dư giữ cho đơn hàng |
| `OrderEscrowStatus.Settled` | `1` | `"Settled"` | Escrow đã giải ngân hết cho seller (hoặc điều chỉnh về 0) |
| `OrderEscrowStatus.Refunded` | `2` | `"Refunded"` | Escrow đã hoàn hết qua luồng refund thông thường |
| `OrderEscrowStatus.Cancelled` | `3` | `"Cancelled"` | Escrow đã bị hủy hoàn toàn và trả về ví người mua |
| `WalletTransactionType.EscrowCancellationRefund` | `19` | `"EscrowCancellationRefund"` | Giao dịch cộng trả toàn bộ số dư khi hủy `OrderEscrow` |
| `AuditAction.OrderEscrowCancel` | `32` | `"OrderEscrowCancel"` | Nhật ký kiểm toán hủy `OrderEscrow` |
| `TransactionStatus.Success` | `1` | `"Success"` | Giao dịch hoàn thành |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)
### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Controller `WalletsController.CancelOrderEscrowInternal` inject `ISender`, bind `orderId` từ route và `Idempotency-Key` từ header qua `command with { OrderId = orderId, IdempotencyKey = idempotencyKey?.Trim() ?? string.Empty }`, bảo vệ bằng `[RequireInternalServiceKey]`.
2. **Bước 2:** `ValidationBehavior<CancelOrderEscrowCommand, CancelOrderEscrowResponse>` kiểm tra DataAnnotations và `ValidateRequest()`.
3. **Bước 3:** Handler mở DB transaction (`BeginTransactionAsync`) và khóa `LockOrderPaymentAsync(request.OrderId)` để tuần tự hóa với `PayOrderWithWallet`, `AdjustOrderPayment`, `SettleSellerOrder`, `RefundOrderPayment`, và concurrent retries.
4. **Bước 4:** Kiểm tra idempotency replay theo `(ReferenceId == OrderId, ClientIdempotencyKey == idempotencyKey)`. Nếu khớp operation `"cancel-order-escrow"` và SHA-256 fingerprint, commit và trả response gốc với `isReplay = true`. Nếu khác operation hoặc khác payload, ném `409 IDEMPOTENCY_KEY_REUSED`.
5. **Bước 5:** Gọi `OrderEscrowAccounting.LoadOrReconcileAsync(...)`. Xác minh escrow tồn tại (`404 ORDER_ESCROW_NOT_FOUND`), chưa `Cancelled` (`409 ORDER_ESCROW_ALREADY_CANCELLED`), chưa `Settled`/`Refunded` (`409 ORDER_ESCROW_ALREADY_RESOLVED`), còn nguyên vẹn `RemainingAmount == OriginalAmount` (`409 ORDER_ESCROW_PARTIALLY_RESOLVED`), và khớp `ExpectedAmount` (`409 ORDER_ESCROW_AMOUNT_MISMATCH`).
6. **Bước 6:** Khóa ví nguồn bằng `LockWalletByIdAsync(escrow.WalletId)` (`SELECT ... FOR UPDATE`), xác minh `ExpectedBuyerId` nếu có (`409 ORDER_ESCROW_OWNERSHIP_CONFLICT`). Cho phép trả tiền về ví `FROZEN` để tiền không bị kẹt trong escrow.
7. **Bước 7:** Trong cùng transaction: gọi `wallet.Deposit(amountToReturn)`, cập nhật `escrow.RemainingAmount = 0m`, `escrow.Status = OrderEscrowStatus.Cancelled`, `escrow.CancelledAt = now`, `escrow.CancelReason = reason`, thêm `WalletTransaction` (`EscrowCancellationRefund`) có `BalanceBefore`/`BalanceAfter`, và ghi `AuditLog` (`OrderEscrowCancel`) qua `_context.AddAuditLog(...)` với `changedColumns` và `traceId`.
8. **Bước 8:** `SaveChangesAsync` + `CommitAsync` và trả về `CancelOrderEscrowResponse` thuần (`200 OK`).

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn (`OrderEscrow`, `OrderEscrowAccounting`, `PayOrderWithWallet`, `AdjustOrderPayment`, `SettleSellerOrder`, `RefundOrderPayment`, `ListTransactions`).
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/checkout/cancel-order-escrow.md`.
- [x] **Persistence:** Đã thêm `OrderEscrowStatus`, cập nhật `OrderEscrow`, `OrderEscrowConfiguration`, enums và migration `AddOrderEscrowCancellationState`.
- [x] **CQRS Structure:** Command, Handler, Response đặt đúng tại `Wallet.Application/Features/Checkout/Commands/CancelOrderEscrow/`.
- [x] **Api:** Action `CancelOrderEscrowInternal` nằm trong `WalletsController`, chỉ inject `ISender`.
- [x] **Verification Script:** Chạy `./scripts/verify-wallet-architecture.sh` báo `OK: Wallet CQRS architecture is intact` (`47` handlers).
- [x] **Tests:** Đã viết unit/integration tests bao phủ happy path, frozen wallet, idempotency, strict all-or-nothing, blocked follow-ups, PostgreSQL concurrency/rollback, và các mã lỗi trong Error Matrix.
- [x] **Build:** `dotnet build Wallet.sln -c Release` và `dotnet test Wallet.sln -c Release` vượt qua 100% với 0 lỗi.
