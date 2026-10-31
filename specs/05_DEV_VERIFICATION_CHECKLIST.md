# 05 — DANH SÁCH KIỂM CHỨNG & NGHIỆM THU (WALLET SERVICE CHECKLIST)

> **Trạng thái triển khai (2026-10-31):** ✅ CLASSIC CQRS — build Release 0 error, 53 test pass, 10 endpoint ánh xạ tới 11 use case (8 Command, 3 Query).
> **Nguồn sự thật:** code (`Wallet.sln`, `Wallet.Application/Features/`, `Wallet.Tests/`).
> **Cập nhật lần cuối:** 2026-10-31.

---

## 1. LỆNH BIÊN DỊCH, CHẠY THỬ VÀ KIỂM CHỨNG

| Bước | Lệnh | Trạng thái |
|:---|:---|:---:|
| Restore | `dotnet restore Wallet.sln` | ✅ |
| Biên dịch | `dotnet build Wallet.sln -c Release` | ✅ (0 error) |
| Kiểm thử | `dotnet test Wallet.sln -c Release` | ✅ (34/34) |
| Bật DB | `docker compose up -d wallet-db` (PostgreSQL `5440`) | ✅ |
| Chạy service | `dotnet run --project Wallet.Api` → `http://localhost:8008` | ✅ |
| Health | `curl http://localhost:8008/health` | ⚠️ không kiểm tra DB |

---

## 2. CHECKLIST TRƯỚC KHI COMMIT

| # | Hạng mục | Cách kiểm chứng | Trạng thái |
|:--|:---|:---|:---:|
| 1 | Build Release 0 error | `dotnet build Wallet.sln -c Release` | ✅ |
| 2 | Test pass 100% | `dotnet test Wallet.sln -c Release` | ✅ |
| 3 | Không còn service facade | `grep -rn "IWalletService\|IAdminWalletService" --include=*.cs .` = 0 | ✅ |
| 4 | Mỗi use case có Handler | `find Wallet.Application/Features -name "*Handler.cs"` = 11 | ✅ |
| 5 | Không dùng Repository Pattern | `grep -rn "IRepository\|UnitOfWork" --include=*.cs .` = 0 | ✅ |
| 6 | Không rò `userId` qua body | controller dùng `TryGetUserId` / `command with { UserId = ... }` | ✅ |
| 7 | Số dư không âm | `WalletAccount.EnsureAvailableBalance` ném `InsufficientBalanceException` | ✅ |
| 8 | Lỗi English sentence case, mã SCREAMING_SNAKE_CASE | đọc `GlobalExceptionHandlerMiddleware.cs`, `DomainException.cs` | ✅ |
| 9 | Không có liên kết trỏ ra ngoài repo | `grep -rn "\.\./\.\./" --include=*.md .` | ✅ |
| 10 | Không commit `bin/`, `obj/`, `.env`, secret | `git status --short` | ✅ |

---

## 3. KIỂM CHỨNG CQRS

```bash
cd src/Services/Wallet

# 1. Không còn feature service facade
grep -rn "interface I.*Service\b" --include="*.cs" Wallet.Application Wallet.Api   # kỳ vọng rỗng

# 2. Đủ 11 use case handler (8 Command, 3 Query)
find Wallet.Application/Features -name "*Handler.cs" | wc -l                       # kỳ vọng 11

# 3. MediatR + ValidationBehavior được đăng ký
grep -n "AddMediatR\|AddOpenBehavior" Wallet.Application/DependencyInjection.cs    # kỳ vọng 2 dòng

# 4. Controller chỉ inject ISender + ILogger
grep -n "private readonly I" Wallet.Api/Controllers/*/*.cs

# 5. Guard kiến trúc đóng băng
./scripts/verify-wallet-architecture.sh                                            # kỳ vọng OK
```

---

## 4. GHI CHÚ HIỆN TRẠNG

* Không có thư mục `Wallet.Infrastructure/Migrations/`; schema đang được quản lý ngoài repo. Mọi thay đổi DB cần phê duyệt riêng.
* `POST /api/v1/wallets/withdrawals` hiện luôn trả lỗi `BUSINESS_RULE_VIOLATION`; luồng tạo yêu cầu rút tiền chưa được bật.
* `/health` trả `Healthy` vô điều kiện, không kiểm tra kết nối DB.
