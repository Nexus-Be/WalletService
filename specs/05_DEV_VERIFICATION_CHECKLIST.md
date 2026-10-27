# 05 — DANH SÁCH KIỂM CHỨNG & NGHIỆM THU (WALLET SERVICE CHECKLIST)

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — có thể build và chạy service bằng Docker/gRPC, nhưng **0 test project** nên `dotnet test` **không thực thi được**, và mọi bước kiểm chứng nghiệp vụ dưới đây phải làm thủ công (không có test tự động nào bảo vệ).
> **Nguồn sự thật:** code (`Wallet.sln`, `Wallet.Api/Program.cs:19,23,47,66`, `Wallet.Infrastructure/InfrastructureDependencyInjection.cs:20-57`), hạ tầng (`Dockerfile`, `docker-compose.yml`).
> **Cập nhật lần cuối:** 2026-10-24.

> **Hiện trạng:** **không có test project** (`Wallet.Tests` không tồn tại, 0 `[Fact]`) ⇒ mọi mục “dotnet test” trong tài liệu cũ **không thể chạy**. Repo cũng **không vendor `dotnet-ef`** (không có `.tools/`) nên phải dùng `dotnet ef` từ máy dev. CI (`.github/workflows/ci-cd.yml`) chỉ build & push Docker image trên nhánh `main`, **không** chạy `dotnet build`/`dotnet test`.

---

## 1. LỆNH BIÊN DỊCH & CHẠY THỬ NGHIỆM

| Bước | Lệnh | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Biên dịch | `dotnet build Wallet.sln -c Release` | ⚠️ chưa có log build trong repo | `Wallet.sln` (4 project, `net8.0`) |
| Bật DB | `docker compose up -d wallet-db` | ✅ | `docker-compose.yml:11-19` (`postgres:16-alpine`, host port `5440`) |
| Áp migration | `dotnet ef database update --project Wallet.Infrastructure --startup-project Wallet.Api` | ✅ (dùng `dotnet ef`, **không** phải `./.tools/dotnet-ef`) | `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs` |
| Chạy service (đúng port `8008`) | `docker compose up -d wallet-service` | ✅ | `docker-compose.yml:31,37`, `Dockerfile:3-4` |
| Chạy service (dev, port **sai chuẩn**) | `dotnet run --project Wallet.Api` → bind `5096`/`7218` | ❌ lệch `8008` | `Wallet.Api/Properties/launchSettings.json:17,27` |
| Health check | `curl http://localhost:8008/health` | ⚠️ trả `Healthy` **kể cả khi DB chết** | `Wallet.Api/Program.cs:66` |
| Gọi thử gRPC | `grpcurl -plaintext localhost:8008 list` (reflection đã bật) rồi `grpcurl -plaintext -d '{"userId":"<uuid>"}' localhost:8008 ShoppingCart.Contracts.V1.WalletApi/GetWallet` | ⚠️ phụ thuộc máy có `grpcurl` | `Wallet.Api/Program.cs:23` (`AddGrpcReflection`), proto `Wallet.Api/Contracts/shop.proto:100-106` |
| Gọi thử qua Gateway | — | ❌ KHÔNG làm được từ repo này | Gateway là repository riêng; repo này không chứa `gateway.runtime.json` |

```bash
# Trình tự tối thiểu để chạy thật (đúng port chuẩn):
cd src/Services/Wallet
docker compose up -d wallet-db          # PostgreSQL 16, host port 5440
dotnet ef database update --project Wallet.Infrastructure --startup-project Wallet.Api
docker compose up -d wallet-service     # gRPC/HTTP trên 8008
curl -s http://localhost:8008/health
```

> **Không có bước nào trong repo tự kiểm tra nghiệp vụ số dư.** Muốn nghiệm thu phải tự gọi `Deposit` → `Transfer` → `Withdraw` → `ListTransactions` và đối chiếu `Wallets.Balance` với tổng `WalletTransactions`.

---

## 2. CHECKLIST TRƯỚC KHI COMMIT CHO AI AGENTS

| # | Hạng mục kiểm tra | Cách kiểm chứng | Trạng thái hiện tại | Bằng chứng (file:line) |
|:--|:---|:---|:---:|:---|
| 1 | Biên dịch thành công | `dotnet build Wallet.sln -c Release` | ⚠️ không có log trong repo | `Wallet.sln` |
| 2 | Không dùng Repository Pattern bọc EF Core | `grep -rn "Repository\|UnitOfWork" --include=*.cs` | ✅ không có kết quả | `Wallet.Application/Common/Interfaces/IWalletDbContext.cs:6` |
| 3 | Không có liên kết trỏ ra ngoài repo | `grep -rn "\.\./\.\./" --include=*.md` | ⚠️ cần chạy lại (bản này đã bỏ hết) | toàn bộ `*.md` |
| 4 | Port: container `8008`, DB `5440` | `grep -n "8008\|5440" docker-compose.yml Dockerfile` | ✅ container / ❌ launchSettings | `docker-compose.yml:19,37`; `Wallet.Api/Properties/launchSettings.json:17,27` |
| 5 | Không có secret plaintext trong file được track | `git ls-files \| grep appsettings` **phải rỗng** | ❌ đang FAIL | `Wallet.Api/appsettings.json:10,16` (được track); `docker-compose.yml:46` (default password) |
| 6 | Bảo vệ số dư: không âm ví | đọc `Wallet.Domain/Entities/WalletAccount.cs:20-48` | ✅ | `WalletAccount.cs:23,32,44` (ném `InsufficientBalanceException`) |
| 7 | Thông báo lỗi 100% English, sentence case | `grep -rnP "[àáảãạăâđêôơư]" --include=*.cs` | ❌ đang FAIL (24 chuỗi) | xem danh sách ở `README.md` mục 2 |
| 8 | Có authentication trước khi expose ra ngoài | `grep -rn "AddAuthentication\|UseAuthentication" --include=*.cs` | ❌ đang FAIL (0 kết quả) | `Wallet.Api/Program.cs` (không có auth middleware) |
| 9 | Test tự động | `dotnet test Wallet.sln` | ❌ không có project test nào | 0 file `*Tests.csproj` |
| 10 | Git sạch, không commit rác | `git status --short` | ✅ | không có `bin/`, `obj/`, `.vs/` trong git |

---

## 3. NHỮNG LỆNH/ĐƯỜNG DẪN KHÔNG DÙNG ĐƯỢC (ĐÃ TỪNG CÓ TRONG TÀI LIỆU CŨ)

| Lệnh / đường dẫn cũ | Vì sao không dùng được | Thay bằng | Bằng chứng (file:line) |
|:---|:---|:---|:---|
| `dotnet test Wallet.sln` | 0 test project | (không có) — chỉ `dotnet build` | không tồn tại `Wallet.Tests` |
| `dotnet test WalletService.sln` | sai tên solution | `Wallet.sln` | `Wallet.sln` |
| `./.tools/dotnet-ef migrations add …` | repo không vendor dotnet-ef | `dotnet ef …` | không có thư mục `.tools/` |
| `gateway.runtime.json` (Mode `Real`, Address `http://localhost:8008`) | file thuộc repo Gateway, không có trong repo này | gọi trực tiếp `grpcurl` tới `8008` | `Wallet.Api/Program.cs:23` (reflection để test trực tiếp) |
| `GET /api/v1/wallet/balance` trên Gateway | endpoint thuộc repo khác; repo này không có controller | `grpcurl … WalletApi/GetWallet` | `Wallet.Api/Program.cs` không có `MapControllers()` |
| `Wallet.Api/Wallet.Api.http` gọi `/weatherforecast/` | endpoint không tồn tại trong service | `GET /health` | `Wallet.Api/Program.cs:66`; `Wallet.Api/Wallet.Api.http:1` |
