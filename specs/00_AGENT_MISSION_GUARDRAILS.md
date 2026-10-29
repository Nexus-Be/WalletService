# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (WALLET SERVICE)

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — 5/5 rpc gRPC và 5/5 handler CQRS là thật, nhưng chỉ 2/8 bảng có đường ghi, consumer Saga chưa từng chạy, **0 test project**, **24 chuỗi lỗi tiếng Việt** và **không có authentication**.
> **Nguồn sự thật:** code (`Wallet.Api/Services/WalletGrpcService.cs:26-160`, `Wallet.Api/Program.cs:47`, `Wallet.Application/Consumers/StockReservedConsumer.cs:13-100`, `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs`).
> **Cập nhật lần cuối:** 2026-10-24.

---

## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)

**WalletService** chịu trách nhiệm độc quyền về tài khoản ví điện tử, số dư khả dụng, giao dịch tiền tệ nội bộ và thanh toán đơn hàng.

| # | Nhiệm vụ | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | Quản lý tài khoản ví (`WalletAccount`): `UserId`, `Balance`, `LockedBalance`, `Currency`, `Status` | ✅ CÓ | `Wallet.Domain/Entities/WalletAccount.cs:8-18`; unique index `Wallet.Infrastructure/Persistence/Configurations/WalletAccountConfiguration.cs:12` |
| 2 | Ghi nhật ký biến động số dư (`WalletTransaction`) | ✅ CÓ | `Wallet.Application/Features/Commands/Deposit/DepositCommand.cs:53-66`, `.../Withdraw/WithdrawCommand.cs:47-58`, `.../Transfer/TransferCommand.cs:60-92` |
| 3 | Quản lý tài khoản ngân hàng liên kết (`BankAccount`) | ⚠️ MỘT PHẦN | Entity + cấu hình + seed tồn tại (`Wallet.Domain/Entities/BankAccount.cs`, `Wallet.Infrastructure/Persistence/Seeds/WalletSeedData.cs:90`) nhưng **không có API/command nào đọc-ghi** bảng này |
| 4 | Tham gia **Checkout Saga**: nghe `StockReservedEvent` để trừ tiền, phát `PaymentCompletedEvent`/`PaymentFailedEvent` | ⚠️ MỘT PHẦN | Consumer đã đăng ký: `Wallet.Application/Consumers/StockReservedConsumer.cs:13`, `Wallet.Infrastructure/InfrastructureDependencyInjection.cs:34`; 4 lời gọi publish thật (`:33,:50,:83,:95`). **Nhưng không có publisher nào phát `IStockReservedEvent`** ⇒ consumer chưa từng chạy |
| 5 | Cung cấp gRPC Server `WalletApi` cho ApiGateway | ✅ CÓ (5/5 rpc) | `Wallet.Api/Services/WalletGrpcService.cs:26,46,66,94,122`; map tại `Wallet.Api/Program.cs:47` |
| 6 | Nạp tiền qua VNPay / rút tiền qua ngân hàng | ❌ CHƯA CÓ | không nơi nào dùng `WalletTopups`, `VnpayIpnLogs`, `WithdrawalRequests`; `WithdrawCommand.cs:55` chỉ nhét `bankAccount` vào `Note` |
| 7 | Ghi vết kiểm toán (`AuditLog`) | ❌ CHƯA CÓ | bảng được khai trong `Wallet.Infrastructure/Persistence/WalletDbContext.cs:21` nhưng 0 lời gọi ghi |

> **Loại giao dịch thật** là enum `WalletTransactionType` gồm 11 giá trị: `TopupVnpay`, `PurchaseDebit`, `RefundCredit`, `EscrowCredit`, `EscrowRelease`, `WithdrawalDebit`, `WithdrawalReversal`, `PlatformFeeCredit`, `AdjustmentCredit`, `AdjustmentDebit`, `DirectVnpayDebit` (`Wallet.Domain/Enums/WalletTransactionType.cs`). **Không tồn tại** các giá trị `DEPOSIT`/`WITHDRAW`/`TRANSFER_IN`/`TRANSFER_OUT`/`ORDER_PAYMENT` như các bản tài liệu cũ mô tả — handler Deposit dùng `TopupVnpay`, Withdraw dùng `WithdrawalDebit`, Transfer dùng `AdjustmentDebit` + `AdjustmentCredit`.

---

## 2. NHỮNG ĐIỀU CẤM KỴ ĐỐI VỚI AI AGENTS (ANTI-HALLUCINATION GUARDRAILS)

| # | Rào chắn | Trạng thái code hiện tại | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | **KHÔNG dùng Repository Pattern bọc EF Core**: dùng trực tiếp `WalletDbContext`/`DbSet<T>` hoặc interface `IWalletDbContext`; cấm `IWalletRepository`, `ITransactionRepository`, `UnitOfWork` | ✅ Tuân thủ | `Wallet.Application/Common/Interfaces/IWalletDbContext.cs:6` (chỉ 4 `DbSet` + `SaveChangesAsync`); handler dùng trực tiếp (`DepositCommand.cs:36-40`) |
| 2 | **Cô lập dữ liệu (database-per-service)**: chỉ sở hữu `wallet_db`, không lưu giỏ hàng/sản phẩm/kho; chỉ lưu `OrderId` dạng tham chiếu | ✅ Tuân thủ | `Wallet.Domain/Entities/DirectPaymentOrder.cs` (`OrderId: Guid`); `Wallet.Infrastructure/Persistence/Seeds/WalletSeedData.cs:106-119` (ReferenceId trỏ SubOrder, không copy dữ liệu) |
| 3 | **KHÔNG để số dư âm**: mọi `Withdraw`/`Transfer`/`Deduct` kiểm tra `Balance >= Amount`, thiếu tiền ném `InsufficientBalanceException` | ✅ Tuân thủ | `Wallet.Domain/Entities/WalletAccount.cs:20-48`; `Wallet.Api/Services/WalletGrpcService.cs` map sang `INSUFFICIENT_BALANCE` tại `Wallet.Api/Middleware/GlobalExceptionHandlerMiddleware.cs:50` |
| 4 | **Cổng `8008` và DB `5440` là bất biến** | ⚠️ Đúng khi chạy container, sai khi `dotnet run` | `Dockerfile:3-4`, `docker-compose.yml:19,37` ✅ vs `Wallet.Api/Properties/launchSettings.json:17,27` = `5096`/`7218` ❌. Mật khẩu DB **không được chép vào tài liệu** — xem `Wallet.Api/appsettings.json:10` |
| 5 | **KHÔNG tự ý sửa Protobuf khi chưa đồng bộ**: rpc/message phải khớp hợp đồng trong repo | ⚠️ Bản trong repo là bản trôi nổi | `Wallet.Api/Contracts/shop.proto` (364 dòng) — md5 `8a7d8698a0607cfb8a41c30a7d7b4492`, **byte-identical với bản của Log service**, khác bản Gateway/User; comment `shop.proto:6` nói bản chuẩn nằm ở Gateway (repo khác — không trích dẫn đường dẫn ở đây). ⇒ Chưa có nguồn sự thật duy nhất |
| 6 | **KHÔNG coi `GET /health` là bằng chứng service hoạt động**: endpoint này không kiểm tra DB | ❌ Đang vi phạm tiềm ẩn | `Wallet.Api/Program.cs:66` trả `Healthy` vô điều kiện; auto-migrate nuốt lỗi chỉ log warning (`Program.cs:49-64`) |
| 7 | **Thông báo lỗi phải 100% tiếng Anh, sentence case** (Nguyên tắc 5 của `AGENTS.md`) | ❌ Đang vi phạm | 24 chuỗi tiếng Việt — danh sách đầy đủ tại `README.md` mục 2, dòng 4 |

---

## 3. ĐIỂM KIỂM CHỨNG 8 TIÊU CHÍ (ĐO TỪ CODE, KHÔNG PHẢI ĐÁNH GIÁ CẢM TÍNH)

| # | Tiêu chí | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | Dễ clone → chạy | ⚠️ | `docker-compose.yml` đủ 2 service; nhưng `launchSettings.json:17,27` bind `5096`/`7218`, `Wallet.Api/Wallet.Api.http:1` còn gọi `/weatherforecast/` (endpoint không tồn tại) |
| 2 | Coupling tầng | ✅ | `Wallet.Application/ApplicationDependencyInjection.cs:17-21`; Application chỉ tham chiếu Domain + `IWalletDbContext` |
| 3 | File đúng chỗ / đồng nhất format | ⚠️ | 6/7 repo backend khác dùng `DependencyInjection.cs`; Wallet + Log dùng `ApplicationDependencyInjection.cs` + `InfrastructureDependencyInjection.cs`; `IWalletDbContext` đặt trong `Common/Interfaces/` |
| 4 | Test độc lập | ❌ | 0 test project, 0 `[Fact]`; không có `Wallet.Tests` ⇒ `dotnet test` không chạy được |
| 5 | Trace/observability | ⚠️ | OTel ✅ `Wallet.Api/Extensions/ObservabilityExtensions.cs:8-25`; nhưng **không đọc `x-trace-id`** từ Gateway và middleware trả `traceId` (`GlobalExceptionHandlerMiddleware.cs:34`) chỉ áp cho HTTP |
| 6 | Tài liệu khớp code | ⚠️ | Bản này đã sửa 6 lỗi sự thật cũ: tên solution (`WalletService.sln` → `Wallet.sln`), tên rpc (`GetBalance/DeductBalance` → `GetWallet/ListTransactions`), tên bảng (`Transactions` → `WalletTransactions`), version MassTransit (8.4.1 → 8.3.0), “có test” → 0 test, “có publish PaymentCompleted/PaymentFailed” → chỉ có trong consumer chưa chạy |
| 7 | Tuân thủ Nguyên tắc 5 (error message) | ❌ | 24 chuỗi tiếng Việt (xem `README.md` mục 2) |
| 8 | Format file | ⚠️ | 3/4 `.csproj` có BOM UTF-8 (`Wallet.Application`, `Wallet.Domain`, `Wallet.Infrastructure`); `Wallet.Api/Wallet.Api.csproj` **không** có BOM |
