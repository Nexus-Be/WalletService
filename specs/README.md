# WALLET SERVICE SPEC KIT — CHỈ DẪN KỸ THUẬT CHO AI AGENT & DEVELOPER

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — bộ spec kit 6 file đã đồng bộ với code: 5/5 rpc gRPC ✅, 5/5 handler CQRS ✅, OpenTelemetry ✅, EF Core + 11 bảng ✅. Nhưng repo vẫn **0 test project**, **24 chuỗi lỗi tiếng Việt**, **không có authentication**, **6/8 bảng không có đường ghi**, consumer `StockReservedConsumer` **chưa từng chạy** (không publisher nào phát `IStockReservedEvent`), và `Wallet.Api/appsettings.json` **chứa mật khẩu thật đang được track**.
> **Nguồn sự thật:** code (`Wallet.Api/Services/WalletGrpcService.cs:26-160`, `Wallet.Api/Program.cs:47`, `Wallet.Infrastructure/Persistence/WalletDbContext.cs:14-21`, `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs`).
> **Cập nhật lần cuối:** 2026-10-24.

Tài liệu này là **Bộ Chỉ Dẫn & Đặc Tả Kỹ Thuật (Spec Kit)** cho repository **WalletService** thuộc hệ sinh thái Microservices **Nexus**. Giúp mọi lập trình viên và AI Coding Agent (Cursor, Claude, Copilot, DeepSeek...) nắm bắt bối cảnh ngay lập tức, hiểu rõ ranh giới nghiệp vụ về ví điện tử, số dư, nạp/rút tiền, điều phối thanh toán trong Checkout Saga và **loại bỏ hoàn toàn nguy cơ ảo giác (zero hallucination)**.

> **Đọc theo thứ tự này**: mỗi mục dưới đây ghi rõ phần nào của code đang là **sự thật** và phần nào **chỉ là thiết kế mong muốn**. Không dùng tài liệu này để suy ra rằng một tính năng đã chạy — hãy mở đúng `file:line` trong cột “Bằng chứng”.

---

## 1. MỤC LỤC BỘ ĐẶC TẢ WALLET SERVICE (SPECS/)

| File Đặc Tả | Trọng Tâm Nội Dung | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| [00_AGENT_MISSION_GUARDRAILS.md](00_AGENT_MISSION_GUARDRAILS.md) | Bounded Context, Port `8008`, DB `wallet_db` (`5440`), các điều cấm kỵ cốt lõi. | ✅ CÓ | `specs/00_AGENT_MISSION_GUARDRAILS.md:1` |
| [01_CLEAN_ARCHITECTURE_LAYERS.md](01_CLEAN_ARCHITECTURE_LAYERS.md) | Cấu trúc 4 tầng Clean Architecture (`.Api`, `.Application`, `.Domain`, `.Infrastructure`), luồng phụ thuộc, No-Repository. | ✅ CÓ | `specs/01_CLEAN_ARCHITECTURE_LAYERS.md:1`, `Wallet.sln` (4 project) |
| [02_API_GRPC_CONTRACTS.md](02_API_GRPC_CONTRACTS.md) | Hợp đồng gRPC `WalletApi` trong `shop.proto`, Request/Response messages, phân trang, tiền tệ, header `x-user-id`. | ✅ CÓ | `Wallet.Api/Contracts/shop.proto:100-106` |
| [03_EVENT_MESSAGING.md](03_EVENT_MESSAGING.md) | Sự kiện bất đồng bộ RabbitMQ + MassTransit 8.3.0 (trừ tiền trong Checkout Saga, phát `PaymentCompleted`/`PaymentFailed`), Outbox/Inbox. | ⚠️ MỘT PHẦN | `Wallet.Application/Consumers/StockReservedConsumer.cs:13-100`; chưa có publisher `IStockReservedEvent` |
| [04_DATA_PERSISTENCE.md](04_DATA_PERSISTENCE.md) | Mô hình thực thể `WalletAccount`, `WalletTransaction`, `BankAccount`…, DbContext EF Core 8 (No Repository), 11 bảng thật. | ⚠️ MỘT PHẦN | `Wallet.Infrastructure/Persistence/WalletDbContext.cs:14-21`; 6/8 bảng không có đường ghi |
| [05_DEV_VERIFICATION_CHECKLIST.md](05_DEV_VERIFICATION_CHECKLIST.md) | Lệnh biên dịch, kiểm chứng thủ công, checklist trước khi commit (không có `dotnet test`). | ⚠️ MỘT PHẦN | `specs/05_DEV_VERIFICATION_CHECKLIST.md:1`; 0 test project |

---

## 2. THÔNG SỐ VẬN HÀNH CHÍNH

| Thông số | Giá trị thật trong repo | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Ngôn ngữ & Runtime | .NET 8 (`net8.0`), C# 12 | ✅ | `Wallet.Api/Wallet.Api.csproj`, `Wallet.Application/Wallet.Application.csproj`, `Wallet.Domain/Wallet.Domain.csproj`, `Wallet.Infrastructure/Wallet.Infrastructure.csproj` (`<TargetFramework>net8.0</TargetFramework>`) |
| Cổng gRPC khi chạy container | `8008` (HTTP/2 + HTTP/1) | ✅ | `Dockerfile:3-4` (`EXPOSE 8008`, `ASPNETCORE_URLS=http://+:8008`), `docker-compose.yml:37` (`${WALLET_SERVICE_PORT:-8008}:8008`) |
| Cổng khi `dotnet run` | `http://localhost:5096` / `https://localhost:7218` | ❌ lệch chuẩn | `Wallet.Api/Properties/launchSettings.json:17,27` |
| Cơ sở dữ liệu | PostgreSQL 16 (`postgres:16-alpine`), DB `wallet_db`, host port `5440` | ✅ | `docker-compose.yml:11-12,19`, `Wallet.Api/appsettings.json:10` |
| Mật khẩu DB / RabbitMQ | **Không in trong tài liệu** — đang nằm dạng plaintext trong file được track | ❌ | `Wallet.Api/appsettings.json:10` (DB), `:16` (RabbitMQ), `docker-compose.yml:46` (giá trị mặc định) |
| Message broker | RabbitMQ, `Host=localhost`, `Port=5672`, vhost `/` | ✅ | `Wallet.Api/appsettings.json:12-16`, `docker-compose.yml:43-46` |
| Đơn vị tiền tệ mặc định | `VND` — mọi message gRPC trả chuỗi decimal 2 chữ số (`InvariantCulture`) | ✅ | `Wallet.Api/Services/WalletGrpcService.cs:39,151,153` |
| OpenTelemetry endpoint | `http://localhost:4317` (OTLP gRPC) | ✅ | `Wallet.Api/appsettings.json:18-19`, `Wallet.Api/Extensions/ObservabilityExtensions.cs:20-24` |
| Phiên bản thư viện chính | MassTransit **8.3.0**, MediatR 12.4.1, FluentValidation 11.9.2, EF Core/Npgsql 8.0.10, Grpc.AspNetCore 2.66.0, OpenTelemetry 1.18.0 | ✅ | 4 file `.csproj` |

---

## 3. NHỮNG THỨ KHÔNG TỒN TẠI TRONG REPO NÀY (ĐỪNG GIẢ ĐỊNH)

| Hạng mục | Trạng thái | Bằng chứng (file:line) |
|:---|:---:|:---|
| Project/kiểm thử tự động (`Wallet.Tests`, `*.Tests.csproj`, `[Fact]`) | ❌ CHƯA CÓ | `git ls-files` không có file test nào; `dotnet test` không có project để chạy |
| Authentication/Authorization (JWT, `AddAuthentication`) | ❌ CHƯA CÓ | grep `AddAuthentication|UseAuthentication|JwtBearer|RequireAuthorization` trên `*.cs` = 0 |
| Endpoint nạp tiền VNPay / IPN handler | ❌ CHƯA CÓ | không nơi nào dùng `WalletTopups`/`VnpayIpnLogs`; `Wallet.Application/Common/Interfaces/IWalletDbContext.cs:6` thậm chí không khai 2 DbSet này |
| API tạo `WithdrawalRequest` (rút tiền qua ngân hàng) | ❌ CHƯA CÓ | `WithdrawCommandHandler` chỉ trừ số dư, không tạo bản ghi (`Wallet.Application/Features/Commands/Withdraw/WithdrawCommand.cs:24-60`) |
| Ghi `AuditLog` | ❌ CHƯA CÓ | grep `AuditLogs` ngoài DbContext/Interface/Migration = 0 |
| Controller REST | ❌ CHƯA CÓ | `Wallet.Api/Program.cs` không gọi `MapControllers()`; chỉ có `GET /health` (`:66`) và `GET /` (`:67`) |
| Retry policy / DLQ cho RabbitMQ | ❌ CHƯA CÓ | grep `UseMessageRetry|UseDelayedRedelivery|ConfigureDeadLetterQueue` = 0 |
| Công cụ `dotnet-ef` được vendor trong repo | ❌ CHƯA CÓ | không có thư mục `.tools/`; dùng `dotnet ef` từ máy dev |
