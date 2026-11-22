# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (WALLET SERVICE)

> **Trạng thái triển khai (2026-11-19):** ✅ CLASSIC CQRS — 68 use case (40 Command, 28 Query) trong `Wallet.Application/Features/`, 2 thin controller inject `ISender`. Không còn gRPC, MassTransit, consumer, Outbox/Inbox.
> **Nguồn sự thật:** code (`Wallet.Api/Controllers/Public/WalletsController.cs`, `Wallet.Application/Features/`, `Wallet.Infrastructure/Persistence/WalletDbContext.cs`).
> **Cập nhật lần cuối:** 2026-11-19.
> **ĐÓNG BĂNG:** chạy `./scripts/verify-wallet-architecture.sh` trước khi sửa.

---

## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)

**WalletService** chịu trách nhiệm độc quyền về tài khoản ví điện tử, số dư khả dụng, giao dịch tiền tệ nội bộ và thanh toán đơn hàng.

| # | Nhiệm vụ | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | Quản lý tài khoản ví (`WalletAccount`): `UserId`, `Balance`, `LockedBalance`, `Currency`, `Status` | ✅ | `Wallet.Domain/Entities/WalletAccount.cs`; unique index `Wallet.Infrastructure/Persistence/Configurations/WalletAccountConfiguration.cs` |
| 2 | Lazy tạo ví khi người dùng chưa có ví | ✅ | `Wallet.Application/Features/Wallets/Queries/GetMyWallet/GetMyWalletQueryHandler.cs` |
| 3 | Ghi nhật ký biến động số dư (`WalletTransaction`) | ✅ | `Wallet.Application/Features/Transfers/Commands/Transfer/TransferCommandHandler.cs` |
| 4 | Nạp tiền qua Stripe (Checkout + webhook + reconciliation) | ✅ | `Wallet.Application/Features/Topups/Commands/TopupStripe/`, `Wallet.Application/Features/Stripe/` |
| 5 | Chuyển tiền nội bộ giữa hai ví | ✅ | `Features/Transfers/Commands/Transfer/TransferCommandHandler.cs` |
| 6 | Thanh toán trực tiếp cho đơn hàng (idempotent theo `OrderId`) | ✅ | `Features/Checkout/Commands/CheckoutDirect/CheckoutDirectCommandHandler.cs` |
| 7 | Duyệt / từ chối yêu cầu rút tiền, điều chỉnh số dư thủ công | ✅ | `Features/AdminWallets/Commands/` |
| 8 | Endpoint tạo/xem/hủy yêu cầu rút tiền và workflow 6 bước cho Admin/Staff | ✅ | `Wallet.Application/Features/Withdrawals/`, `Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/` |
| 9 | Quản lý tài khoản ngân hàng liên kết (`BankAccount`) & mặc định khi rút tiền | ✅ | `Wallet.Domain/Entities/BankAccount.cs`, `Wallet.Application/Features/BankAccounts/` |
| 10 | Ghi vết kiểm toán (`AuditLog`) qua `AddAuditLog(...)` & phát REST Notification (`INotificationClient`) | ✅ | `Wallet.Application/Common/Extensions/AuditLogExtensions.cs`, `Wallet.Infrastructure/Services/Notification/NotificationClient.cs` |
| 11 | gRPC server, MassTransit consumer, Saga, Outbox/Inbox | 🛑 ĐÃ XÓA | không còn file/thư mục tương ứng |
| 12 | Khóa, mở khóa, capture và hoàn cọc đấu giá từ ví | ✅ | `Wallet.Application/Features/Holds/`, `docs/api/holds/auction-deposits.md` |

---

## 2. NHỮNG ĐIỀU CẤM KỴ ĐỐI VỚI AI AGENTS (ANTI-HALLUCINATION GUARDRAILS)

| # | Rào chắn | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | **CQRS bắt buộc**: mỗi endpoint là một Command/Query + Handler. Cấm tạo `I{Feature}Service` facade hoặc `Wallet.Application/Services/` | ✅ | `Wallet.Application/Features/**` |
| 2 | **Cấm Repository Pattern bọc EF Core**: handler inject trực tiếp `IWalletDbContext` | ✅ | `Wallet.Application/Interfaces/IWalletDbContext.cs` |
| 3 | **Cô lập dữ liệu**: chỉ sở hữu `wallet_db`; đơn hàng chỉ lưu `ReferenceId`/`OrderId` | ✅ | `Wallet.Domain/Entities/WalletTransaction.cs`, `DirectPaymentOrder.cs` |
| 4 | **Không cho số dư âm**: `Withdraw`/`Deduct`/`Transfer` kiểm tra `AvailableBalance >= Amount` | ✅ | `Wallet.Domain/Entities/WalletAccount.cs` (`EnsureAvailableBalance`) |
| 5 | **Port `8008` và DB `5440` là bất biến** | ✅ | `docker-compose.yml`, `Dockerfile` |
| 6 | **Không tin `userId` từ body**: controller lấy từ `HttpContext.GetUserId()` hoặc header nội bộ | ✅ | `Wallet.Api/Extensions/HttpContextExtensions.cs`, `WalletsController.cs` |
| 7 | **Không đổi route/JSON contract** khi chưa được duyệt | ✅ | `Wallet.Api/Controllers/Public/` |
| 8 | **Thông báo lỗi 100% English, sentence case; mã lỗi SCREAMING_SNAKE_CASE** | ✅ | `Wallet.Api/Middleware/GlobalExceptionHandlerMiddleware.cs`, `Wallet.Domain/Exceptions/DomainException.cs` |

---

## 3. ĐIỂM KIỂM CHỨNG BẮT BUỘC

| # | Tiêu chí | Trạng thái | Bằng chứng |
|:--|:---|:---:|:---|
| 1 | Kiến trúc CQRS nguyên vẹn | ✅ | `./scripts/verify-wallet-architecture.sh` |
| 2 | Build Release 0 error | ✅ | `dotnet build Wallet.sln -c Release` |
| 3 | Test pass 100% | ✅ | `dotnet test Wallet.sln -c Release` |
| 4 | Không có liên kết trỏ ra ngoài repo | ✅ | toàn bộ `*.md` dùng đường dẫn nội bộ |
| 5 | Không commit secret hay file rác | ✅ | `.gitignore`, không có `bin/`, `obj/` trong git |

---

## 4. DANH MỤC FILE BẮT BUỘC ĐỌC

1. `specs/01_CLEAN_ARCHITECTURE_LAYERS.md` — 4 tầng và layout CQRS.
2. `specs/02_API_REST_CONTRACTS.md` — endpoint và handler tương ứng.
3. `specs/03_EVENT_MESSAGING.md` — bãi bỏ broker; MediatR chỉ là in-process.
4. `specs/04_DATA_PERSISTENCE.md` — schema PostgreSQL.
5. `specs/05_DEV_VERIFICATION_CHECKLIST.md` — lệnh build/test.
