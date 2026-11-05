# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (WALLET SERVICE)

> **Trạng thái triển khai (2026-10-31):** ✅ CLASSIC CQRS — 11 use case (8 Command, 3 Query) trong `Wallet.Application/Features/`, 3 thin controller inject `ISender`, 53 test pass. Không còn gRPC, MassTransit, consumer, Outbox/Inbox.
> **Nguồn sự thật:** code (`Wallet.Api/Controllers/Public/WalletsController.cs`, `Wallet.Application/Features/`, `Wallet.Infrastructure/Persistence/WalletDbContext.cs`).
> **Cập nhật lần cuối:** 2026-10-31.
> **ĐÓNG BĂNG:** chạy `./scripts/verify-wallet-architecture.sh` trước khi sửa.

---

## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)

**WalletService** chịu trách nhiệm độc quyền về tài khoản ví điện tử, số dư khả dụng, giao dịch tiền tệ nội bộ và thanh toán đơn hàng.

| # | Nhiệm vụ | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | Quản lý tài khoản ví (`WalletAccount`): `UserId`, `Balance`, `LockedBalance`, `Currency`, `Status` | ✅ | `Wallet.Domain/Entities/WalletAccount.cs`; unique index `Wallet.Infrastructure/Persistence/Configurations/WalletAccountConfiguration.cs` |
| 2 | Lazy tạo ví khi người dùng chưa có ví | ✅ | `Wallet.Application/Features/Wallets/Queries/GetMyWallet/GetMyWalletQueryHandler.cs` |
| 3 | Ghi nhật ký biến động số dư (`WalletTransaction`) | ✅ | `Features/Transfers/Commands/Transfer/TransferCommandHandler.cs`, `Features/VnPay/Commands/ProcessVnPayIpn/ProcessVnPayIpnCommandHandler.cs` |
| 4 | Nạp tiền qua VNPay (tạo URL + IPN + return callback) | ✅ | `Features/Topups/Commands/TopupVnPay/`, `Features/VnPay/` |
| 5 | Chuyển tiền nội bộ giữa hai ví | ✅ | `Features/Transfers/Commands/Transfer/TransferCommandHandler.cs` |
| 6 | Thanh toán trực tiếp cho đơn hàng (idempotent theo `OrderId`) | ✅ | `Features/Checkout/Commands/CheckoutDirect/CheckoutDirectCommandHandler.cs` |
| 7 | Duyệt / từ chối yêu cầu rút tiền, điều chỉnh số dư thủ công | ✅ | `Features/AdminWallets/Commands/` |
| 8 | Endpoint tạo yêu cầu rút tiền cho người dùng | ⚠️ BỊ VÔ HIỆU | `Features/Withdrawals/Commands/RequestWithdrawal/RequestWithdrawalCommandHandler.cs` luôn ném `BUSINESS_RULE_VIOLATION` |
| 9 | Quản lý tài khoản ngân hàng liên kết (`BankAccount`) | ⚠️ CHỈ CÓ ENTITY | `Wallet.Domain/Entities/BankAccount.cs`; chưa có API đọc/ghi |
| 10 | Ghi vết kiểm toán (`AuditLog`) qua `AddAuditLog(...)` | ✅ | `Wallet.Application/Common/Extensions/AuditLogExtensions.cs` |
| 11 | gRPC server, MassTransit consumer, Saga, Outbox/Inbox | 🛑 ĐÃ XÓA | không còn file/thư mục tương ứng |

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
| 3 | Test pass 100% | ✅ | `Wallet.Tests` (53 tests) |
| 4 | Không có liên kết trỏ ra ngoài repo | ✅ | toàn bộ `*.md` dùng đường dẫn nội bộ |
| 5 | Không commit secret hay file rác | ✅ | `.gitignore`, không có `bin/`, `obj/` trong git |

---

## 4. DANH MỤC FILE BẮT BUỘC ĐỌC

1. `specs/01_CLEAN_ARCHITECTURE_LAYERS.md` — 4 tầng và layout CQRS.
2. `specs/02_API_REST_CONTRACTS.md` — 10 endpoint và handler tương ứng.
3. `specs/03_EVENT_MESSAGING.md` — bãi bỏ broker; MediatR chỉ là in-process.
4. `specs/04_DATA_PERSISTENCE.md` — schema PostgreSQL.
5. `specs/05_DEV_VERIFICATION_CHECKLIST.md` — lệnh build/test.
