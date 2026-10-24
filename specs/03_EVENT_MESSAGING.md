# 03 — SỰ KIỆN BẤT ĐỒNG BỘ QUA RABBITMQ (WALLET SERVICE EVENT MESSAGING)

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — consumer `StockReservedConsumer:IConsumer<IStockReservedEvent>` đã đăng ký và có **4 lời gọi publish thật** cho `IPaymentCompletedEvent`/`IPaymentFailedEvent` (`Wallet.Application/Consumers/StockReservedConsumer.cs:33,50,83,95`), Outbox/Inbox đã cấu hình. Nhưng **không publisher nào phát `IStockReservedEvent`** ⇒ consumer **chưa từng chạy**; không có `UseMessageRetry`/DLQ; 3 interface event **không có lớp implementation**.
> **Nguồn sự thật:** code (`Wallet.Application/Consumers/StockReservedConsumer.cs:13-102`, `Wallet.Domain/Events/WalletEvents.cs:3-25`, `Wallet.Infrastructure/InfrastructureDependencyInjection.cs:30-57`, `Wallet.Infrastructure/Persistence/WalletDbContext.cs:31-33`).
> **Cập nhật lần cuối:** 2026-10-24.

> **Hiện trạng (đính chính tài liệu cũ):** các bản trước nói “khai báo event nhưng **0 lời gọi publish**” — **sai**. Có 4 lời gọi publish trong consumer (1 `PaymentCompleted` + 3 `PaymentFailed`). Vấn đề thật nằm ở **đầu vào**: không service nào trong hệ sinh thái phát `IStockReservedEvent`, nên toàn bộ luồng Saga này là code chết khi chạy thật.

WalletService được thiết kế đóng vai trò bước thanh toán của **Checkout Saga**.

---

## 1. SỰ KIỆN LẮNG NGHE (CONSUMERS)

| Sự kiện | Consumer | Đăng ký | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---|:---:|:---|
| `IStockReservedEvent` | `StockReservedConsumer` | `x.AddConsumer<StockReservedConsumer>()` + `ConfigureEndpoints` | ⚠️ CÓ code, **chưa từng chạy** | `Wallet.Application/Consumers/StockReservedConsumer.cs:13`; `Wallet.Infrastructure/InfrastructureDependencyInjection.cs:34,55`; publisher trong `src/` = **0** (grep `IStockReservedEvent`/`StockReservedEvent` ngoài Wallet) |
| `x-user-id` / trace header từ Gateway | — | — | ❌ CHƯA CÓ | repo không đọc `x-trace-id`; không có filter nào set `Activity` từ header |

**Hợp đồng message** (`Wallet.Domain/Events/WalletEvents.cs:18-25`): `OrderId`, `ReservationId`, `UserId`, `TotalAmount` (string), `Timestamp`.

**Luồng xử lý thật** (`StockReservedConsumer.cs:24-102`):

| Bước | Hành vi | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| 1 | Parse `TotalAmount` theo `InvariantCulture`; nếu ≤ 0 hoặc không parse được → publish `IPaymentFailedEvent` với lý do **tiếng Việt** `"Số tiền thanh toán không hợp lệ"` | ⚠️ (vi phạm Nguyên tắc 5) | `:30-40` (publish `:33`, lý do `:36`) |
| 2 | Parse `UserId` → tìm ví | ✅ | `:42-45` |
| 3 | Không có ví → publish `IPaymentFailedEvent` (`"Người dùng chưa có tài khoản ví"`) và `return` — **không** tạo ví | ⚠️ | `:47-57` (publish `:50`, lý do `:53`) |
| 4 | Đủ tiền: `wallet.Deduct(requiredAmount)` + ghi `WalletTransaction` type **`PurchaseDebit`** (không phải `ORDER_PAYMENT` như tài liệu cũ) | ✅ | `:61-77` (`Note` tiếng Việt tại `:74`) |
| 5 | `SaveChangesAsync` rồi publish `IPaymentCompletedEvent` | ⚠️ thứ tự đáng lưu ý | `:78` (save) → `:83-89` (publish) — publish **sau** khi save, không có transaction tường minh |
| 6 | Thiếu tiền (`InsufficientBalanceException`) → publish `IPaymentFailedEvent` (`"Số dư ví không đủ để thanh toán đơn hàng"`), **không** ghi giao dịch | ⚠️ | `:91-101` (publish `:95`, lý do `:98`) |

> **Không có nhánh nào khác**: mọi exception khác (ví dụ lỗi DB) **không** được bắt ⇒ message sẽ bị RabbitMQ redeliver vô hạn (không có retry policy/DLQ, xem mục 3).

---

## 2. SỰ KIỆN PHÁT HÀNH (EVENTS PUBLISHED)

| Sự kiện | Khai báo | Lời gọi publish thật | Payload thật | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---|:---|:---:|:---|
| `IPaymentCompletedEvent` | interface, 4 property | 1 | `OrderId`, `TransactionId`, `Amount` (chuỗi `0.00`), `Timestamp` | ⚠️ CÓ code, chưa từng chạy | Khai báo `Wallet.Domain/Events/WalletEvents.cs:3-9`; publish `StockReservedConsumer.cs:83-89` |
| `IPaymentFailedEvent` | interface, 3 property | 3 (3 lý do khác nhau) | `OrderId`, `Reason`, `Timestamp` | ⚠️ CÓ code, chưa từng chạy | Khai báo `WalletEvents.cs:11-16`; publish `StockReservedConsumer.cs:33-38`, `:50-55`, `:95-100` |
| `IStockReservedEvent` | interface, 5 property | 0 (chỉ consume) | — | ❌ chưa có publisher | Khai báo `WalletEvents.cs:18-25`; consumer `StockReservedConsumer.cs:13` |

> **Không có lớp implementation nào** cho cả 3 interface — chỉ có interface trong `Wallet.Domain/Events/WalletEvents.cs`. Consumer publish bằng **anonymous object** (`context.Publish<IPaymentCompletedEvent>(new { ... })`). Đây là rủi ro hợp đồng: bên consume phải có type cụ thể mới deserialize được, mà repo này không có.

**Ai tiêu thụ event của Wallet?** — Không xác định được trong phạm vi repo này (mỗi service là repository độc lập; tài liệu này không trích dẫn sang repo khác). Muốn kiểm chứng phải mở đúng repo đó và grep `IPaymentCompletedEvent`/`PaymentCompletedEvent`.

---

## 3. ĐẢM BẢO TÍNH TOÀN VẸN (IDEMPOTENCY & OUTBOX)

| Hạng mục | Thực tế trong code | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| EF Core Outbox của MassTransit | Đã bật: `AddEntityFrameworkOutbox<WalletDbContext>` + `UsePostgres()` + `UseBusOutbox()` | ✅ | `Wallet.Infrastructure/InfrastructureDependencyInjection.cs:36-40` |
| Bảng Outbox/Inbox được map vào DbContext | Đủ 3: `AddInboxStateEntity`, `AddOutboxMessageEntity`, `AddOutboxStateEntity` | ✅ | `Wallet.Infrastructure/Persistence/WalletDbContext.cs:31-33` |
| Bảng tồn tại trong migration | `InboxState` (`:85`), `OutboxState` (`:108`), `OutboxMessage` (`:146`) | ✅ | `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs` |
| **Nguyên tử**: trừ tiền + phát event trong **một** transaction DB | Chỉ dựa vào một `SaveChangesAsync` (`StockReservedConsumer.cs:78`); publish gọi **sau** đó (`:83`) và **không** có `BeginTransaction` nào trong repo | ⚠️ Chưa chứng minh | grep `BeginTransaction|Database.BeginTransactionAsync` trên `*.cs` = 0 |
| **Idempotent consumer** (bảng `InboxState`) | Bật same-endpoint inbox qua `AddEntityFrameworkOutbox`; chưa có test chứng minh chặn trùng | ⚠️ | `InfrastructureDependencyInjection.cs:36-40` |
| **Retry policy / DLQ** | Không có `UseMessageRetry`, `UseDelayedRedelivery`, `ConfigureDeadLetterQueue`, không có `cfg.ReceiveEndpoint(..., e => e.UseMessageRetry(...))` | ❌ CHƯA CÓ | `InfrastructureDependencyInjection.cs:42-56` chỉ có `Host` + `ConfigureEndpoints` |
| **Endpoint naming** | Kebab-case | ✅ | `InfrastructureDependencyInjection.cs:32` (`SetKebabCaseEndpointNameFormatter`) |
| **Ghi chú lỗi thời trong code** | Comment ghi `// MassTransit 8.4.1 / 8.3.0` trong khi package thật là `8.3.0` | ⚠️ | `InfrastructureDependencyInjection.cs:29`; `Wallet.Infrastructure/Wallet.Infrastructure.csproj:9` |
