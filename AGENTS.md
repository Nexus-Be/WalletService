<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **WalletService** (474 symbols, 755 relationships, 11 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> Index stale? Run `node .gitnexus/run.cjs analyze` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? `npx gitnexus analyze` (npm 11 crash → `npm i -g gitnexus`; #1939).

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows. For regression review, compare against the default branch: `detect_changes({scope: "compare", base_ref: "main"})`.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `query({search_query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `context({name: "symbolName"})`.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method without first running `impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit changes without running `detect_changes()` to check affected scope.

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/WalletService/context` | Codebase overview, check index freshness |
| `gitnexus://repo/WalletService/clusters` | All functional areas |
| `gitnexus://repo/WalletService/processes` | All execution flows |
| `gitnexus://repo/WalletService/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

---

# QUY CHUẨN VẬN HÀNH DÀNH CHO AI CODING AGENTS (WALLET SERVICE)

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — bộ quy chuẩn này đã được đối chiếu với code thật: 5/5 rpc gRPC ✅, 5/5 handler CQRS ✅, OpenTelemetry ✅; nhưng **6/8 bảng không có đường ghi**, consumer Saga **chưa từng chạy**, **0 test project**, **24 chuỗi thông báo lỗi tiếng Việt** và **không có authentication**. Mọi con số dưới đây đo trên code, không phải thiết kế mong muốn.
> **Nguồn sự thật:** code (`Wallet.sln`, `Wallet.Api/Services/WalletGrpcService.cs:26-160`, `Wallet.Application/Consumers/StockReservedConsumer.cs:13-102`, `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs`).
> **Cập nhật lần cuối:** 2026-10-24.

---

## 🛑 NGUYÊN TẮC 1: BẮT BUỘC LẬP KẾ HOẠCH TRƯỚC KHI HÀNH ĐỘNG (PLAN-FIRST RULE)

Mọi AI Agent khi nhận nhiệm vụ kỹ thuật tại WalletService **BẮT BUỘC PHẢI LẬP VÀ TRÌNH BÀY PLAN** trước khi thực hiện bất kỳ hành động sửa đổi mã nguồn, thêm/xóa file, refactor, tạo migration database hay thay đổi cấu hình nào.

Một Plan chuẩn bắt buộc phải có:
1. **Mục tiêu (Objective)**: Tóm tắt chính xác điều cần làm.
2. **Context & Files cần đọc**: Liệt kê các file nội bộ của WalletService cần đọc theo đúng ma trận đọc file tại Nguyên tắc 3.
3. **Phân tích rủi ro & Phạm vi ảnh hưởng (Impact Analysis)**:
   - Dùng GitNexus `impact` đánh giá blast radius các symbol bị ảnh hưởng.
   - Cảnh báo rõ nếu mức độ rủi ro là HIGH hoặc CRITICAL.
4. **Checklist các bước thực hiện (Step-by-Step Implementation)**: Từng bước logic, chi tiết.
5. **Kế hoạch kiểm chứng (Verification)**: `dotnet build Wallet.sln -c Release` + cách kiểm chứng thủ công theo `specs/05_DEV_VERIFICATION_CHECKLIST.md`. **Lưu ý: repo hiện KHÔNG có test project nên `dotnet test` không thực thi được** — đừng ghi `dotnet test` vào plan như một bước sẽ chạy.

---

## 🧭 NGUYÊN TẮC 2: QUY TẮC PHẠM VI NỘI BỘ (INWARD-ONLY / SELF-CONTAINED)

WalletService là một Git Repository độc lập (`WalletService.git`):
- **TUYỆT ĐỐI CHỈ ĐƯỢC trỏ tới các tài nguyên nội bộ của WalletService**.
- **TUYỆT ĐỐI KHÔNG ĐƯỢC trỏ ngược ra ngoài Root** (`../../../docs/...`) hay sang các Service khác (`../../Catalog/...`).
- Toàn bộ tài liệu, hướng dẫn và cấu hình trong repo này phải 100% tự cung tự cấp (Self-contained).

> Áp dụng ngay cả khi bạn biết một sự thật nằm ở repo khác (ví dụ: bản `shop.proto` chuẩn nằm ở ApiGateway). Cách viết đúng: **mô tả hiện trạng trong repo này** + nói “bản chuẩn nằm ở repository của ApiGateway”, **không** ghi đường dẫn `../...` ra ngoài.

---

## 📚 NGUYÊN TẮC 3: DANH MỤC FILE NỘI BỘ BẮT BUỘC PHẢI ĐỌC TẠI WALLET SERVICE

Khi bắt đầu làm việc tại WalletService, AI Agent **BẮT BUỘC PHẢI ĐỌC** các file nội bộ sau theo thứ tự:

| # | File | Nội dung phải nắm | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---|:---:|:---|
| 1 | `specs/00_AGENT_MISSION_GUARDRAILS.md` | Bounded context số dư ví, nạp/rút/chuyển tiền, thanh toán; port `8008`; DB `5440` (`wallet_db`) | ✅ | `specs/00_AGENT_MISSION_GUARDRAILS.md:1` |
| 2 | `specs/01_CLEAN_ARCHITECTURE_LAYERS.md` | 4 tầng Clean Architecture, No-Repository pattern over EF Core, danh mục index thật | ✅ | `specs/01_CLEAN_ARCHITECTURE_LAYERS.md:1` |
| 3 | `specs/02_API_GRPC_CONTRACTS.md` | Hợp đồng gRPC `WalletApi`: `GetWallet`, `Deposit`, `Withdraw`, `Transfer`, `ListTransactions` | ✅ | `Wallet.Api/Contracts/shop.proto:100-106` |
| 4 | `specs/03_EVENT_MESSAGING.md` | RabbitMQ/MassTransit 8.3.0: consumer `StockReservedEvent`, publish `PaymentCompleted`/`PaymentFailed`, Outbox/Inbox | ⚠️ | `Wallet.Application/Consumers/StockReservedConsumer.cs:13` |
| 5 | `specs/04_DATA_PERSISTENCE.md` | 8 entity / 8 `DbSet` / 11 bảng thật; chỉ 2/8 bảng có đường ghi | ⚠️ | `Wallet.Infrastructure/Persistence/WalletDbContext.cs:14-21` |
| 6 | `specs/05_DEV_VERIFICATION_CHECKLIST.md` | Lệnh build, chạy Docker, kiểm chứng thủ công; danh sách lệnh KHÔNG dùng được | ✅ | `specs/05_DEV_VERIFICATION_CHECKLIST.md:1` |

---

## 🔒 NGUYÊN TẮC 4: RÀO CHẮN BẤT BIẾN TẠI WALLET SERVICE

1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**: Sử dụng trực tiếp `WalletDbContext` và `DbSet<T>` (hoặc interface `IWalletDbContext`) trong tầng Application. Cấm tạo `IWalletRepository`, `ITransactionRepository`, `UnitOfWork`.
   — *Hiện trạng:* ✅ tuân thủ (`Wallet.Application/Common/Interfaces/IWalletDbContext.cs:6`).
2. **TUYỆT ĐỐI KHÔNG ĐỂ SỐ DƯ ÂM (NEGATIVE BALANCE)**: Mọi thao tác rút, chuyển hoặc trừ tiền thanh toán bắt buộc kiểm tra `Balance >= Amount`. Thiếu tiền phải ném ngoại lệ không cho phép giao dịch.
   — *Hiện trạng:* ✅ (`Wallet.Domain/Entities/WalletAccount.cs:20-48`).
3. **CẤU HÌNH PORT 8008 VÀ DATABASE 5440 LÀ BẤT BIẾN**.
   — *Hiện trạng:* ✅ khi chạy container (`Dockerfile:3-4`, `docker-compose.yml:19,37`); ❌ khi `dotnet run` vì `Wallet.Api/Properties/launchSettings.json:17,27` bind `5096`/`7218`. **Sửa `launchSettings.json` là việc PHẢI làm, không phải tuỳ chọn.**
4. **HỢP ĐỒNG PROTOBUF PHẢI NẰM TRONG REPO VÀ PHẢI ĐƯỢC ĐỐI CHIẾU TRƯỚC KHI SỬA**: file hợp đồng của service là `Wallet.Api/Contracts/shop.proto`; sửa rpc/message mà chưa đối chiếu với bản chuẩn (thuộc repository ApiGateway) là **cấm**.
   — *Hiện trạng:* ⚠️ bản trong repo là bản trôi nổi — md5 `8a7d8698a0607cfb8a41c30a7d7b4492`, byte-identical với bản của Log service, khác bản Gateway/User ⇒ **chưa có nguồn sự thật duy nhất**.
5. **CẤM TIN HEADER NHƯ MỘT CƠ CHẾ BẢO MẬT**: `x-user-id` chỉ là metadata Gateway chuyển xuống; không được dùng nó thay cho xác thực.
   — *Hiện trạng:* ❌ service **không có authentication** nhưng vẫn ưu tiên `x-user-id` (`Wallet.Api/Services/WalletGrpcService.cs:28,48,68,96,124`). Không được viết tài liệu mô tả đây là “bảo mật”.
6. **CẤM MÔ TẢ TÍNH NĂNG CHƯA CÓ LÀ ĐÃ CÓ**: VNPay topup/IPN, rút tiền qua ngân hàng (`WithdrawalRequest`), `AuditLog` **chưa được triển khai** (0 đường ghi). Xem `specs/04_DATA_PERSISTENCE.md` mục 3.
7. **CẤM COMMIT SECRET**: `Wallet.Api/appsettings.json` hiện **đang được track** và chứa mật khẩu DB/RabbitMQ (`:10`,`:16`). Mọi thay đổi tiếp theo không được thêm secret mới; việc gỡ file này khỏi git là P0.

---

## 💬 NGUYÊN TẮC 5: QUY CHUẨN THÔNG BÁO LỖI TOÀN HỆ THỐNG (MANDATORY ERROR RULES)

Tất cả các thông báo lỗi (Error Messages, Exceptions, ProblemDetails, Validation Errors, Response Details) **BẮT BUỘC** phải tuân theo 3 nguyên tắc sau:
1. **Ngôn ngữ (100% English)**: Toàn bộ thông báo lỗi phải được viết bằng tiếng Anh. Tuyệt đối không dùng tiếng Việt trong chuỗi thông báo lỗi trả về cho client.
2. **Quy cách viết hoa (Sentence case)**: Viết hoa duy nhất chữ cái đầu câu, các từ tiếp theo viết thường (ngoại trừ tên riêng, từ viết tắt hoặc định danh kỹ thuật như `ID`, `OTP`, `JWT`, `URL`, `UUID`, `Email`...).
   - ✅ Đúng: `"Wallet account not found for the specified user."`
   - ✅ Đúng: `"Insufficient balance to complete the transaction."`
   - ❌ Sai: `"Số dư không đủ"` (Không dùng tiếng Việt)
   - ❌ Sai: `"wallet not found"` (Chữ cái đầu không viết hoa)
   - ❌ Sai: `"WALLET NOT FOUND"` (Cấm viết hoa toàn bộ)
   - ❌ Sai: `"Wallet Not Found Provided"` (Không dùng Title Case)
3. **Mã lỗi (Error Codes — SCREAMING_SNAKE_CASE)**: Mã lỗi luôn viết hoa toàn bộ và nối bằng dấu gạch dưới:
   - Ví dụ: `WALLET_NOT_FOUND`, `INSUFFICIENT_BALANCE`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`.

> **Hiện trạng vi phạm: 24 chuỗi tiếng Việt** trong ngữ cảnh lỗi — `Wallet.Api/Services/WalletGrpcService.cs:31,51,61,71,89,99,117`; `Wallet.Domain/Entities/WalletAccount.cs:23,32,44`; `Wallet.Application/Consumers/StockReservedConsumer.cs:32,49,93`; `Wallet.Domain/Exceptions/DomainException.cs:25,31`; `Wallet.Application/Features/Commands/Deposit/DepositCommand.cs:17,18`; `.../Withdraw/WithdrawCommand.cs:18,19,20`; `.../Transfer/TransferCommand.cs:18,19,20,21`. Cộng thêm 3 `Reason` tiếng Việt trong payload `IPaymentFailedEvent` (`StockReservedConsumer.cs:36,53,98`). **Không** sửa chúng bằng cách dịch máy — phải giữ nguyên nghĩa nghiệp vụ và chuyển sang sentence case tiếng Anh.

---

## 🎯 NGUYÊN TẮC 6: ĐIỀU KIỆN HOÀN THÀNH NHIỆM VỤ (DEFINITION OF DONE)

Một task tại WalletService chỉ hoàn tất khi:
- [ ] Đã lập Plan chi tiết trước khi thực hiện.
- [ ] Không chứa bất kỳ liên kết trỏ ngược ra ngoài WalletService (`grep -rn "\.\./\.\./" --include=*.md` phải rỗng).
- [ ] `dotnet build Wallet.sln -c Release` thành công 100% (0 error) — **tên solution là `Wallet.sln`**, không phải `WalletService.sln`.
- [ ] **Ghi rõ trạng thái test**: repo hiện có **0 test project** ⇒ không được tuyên bố “test pass”. Nếu bạn thêm test, phải cập nhật `specs/05_DEV_VERIFICATION_CHECKLIST.md` cùng lúc.
- [ ] Không thêm bất kỳ secret/connection string nào vào file được track (`git ls-files | grep -i appsettings` phải rỗng).
- [ ] Trạng thái Git sạch, không commit file rác (`bin/`, `obj/`, `.vs/`, `.idea/`).

---

## 🧪 NGUYÊN TẮC 7: HIỆN TRẠNG ĐÃ KIỂM CHỨNG (BẮT BUỘC ĐỌC TRƯỚC KHI CODE)

> Số liệu đo trên **46 file `.cs` / 1755 dòng** (không tính `Migrations`) của repo này, ngày 2026-10-24. Đây là sự thật đã kiểm chứng bằng cách mở đúng `file:line`, không phải mô tả kiến trúc.

| Hạng mục | Số thật | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| rpc gRPC đã implement | **5/5** | ✅ | `Wallet.Api/Services/WalletGrpcService.cs:26,46,66,94,122`; map `Wallet.Api/Program.cs:47` |
| Handler CQRS (MediatR) | **5/5** | ✅ | `Wallet.Application/Features/Commands/Deposit/DepositCommand.cs:22` (+ Withdraw, Transfer, GetWallet, ListTransactions) |
| OpenTelemetry | **1** pipeline | ✅ | `Wallet.Api/Extensions/ObservabilityExtensions.cs:8-25` |
| Serilog | **0** | ❌ | grep `Serilog` toàn repo = 0 |
| Authentication/Authorization | **0** | ❌ | không có `AddAuthentication`/`UseAuthentication`/`JwtBearer`; tin `x-user-id` tại `WalletGrpcService.cs:28,48,68,96,124` |
| Test project / test case | **0 / 0** | ❌ | không có `Wallet.Tests`; CI `.github/workflows/ci-cd.yml` chỉ build Docker image |
| Bảng có đường ghi trong code | **2/8** (`Wallets`, `WalletTransactions`) | ❌ | 6 bảng còn lại: `WalletTopups`, `BankAccounts`, `DirectPaymentOrders`, `VnpayIpnLogs`, `AuditLogs`, `WithdrawalRequests` — grep ngoài DbContext/Interface/Migration = 0 |
| Consumer đã từng chạy thật | **0** | ❌ | `StockReservedConsumer` đã đăng ký nhưng **không publisher nào phát `IStockReservedEvent`** |
| Lời gọi publish event | **4** (1 completed + 3 failed) | ⚠️ | `Wallet.Application/Consumers/StockReservedConsumer.cs:33,50,83,95` |
| Retry policy / DLQ | **0** | ❌ | `Wallet.Infrastructure/InfrastructureDependencyInjection.cs:42-56` chỉ có `Host` + `ConfigureEndpoints` |
| Chuỗi lỗi tiếng Việt | **24** | ❌ | xem Nguyên tắc 5 |
| Port local khớp `8008` | **0** (`5096`/`7218`) | ❌ | `Wallet.Api/Properties/launchSettings.json:17,27` |
| File secret được track | **1** (`Wallet.Api/appsettings.json`) | ❌ | `git ls-files` + `Wallet.Api/appsettings.json:10,16`; default password trong `docker-compose.yml:46` |
| Migration | **1** (`InitialWalletSchema`, 11 bảng) | ✅ | `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs` |
| Index `(UserId, CreatedAt)` trên `WalletTransactions` | **0** (chỉ có `IX_WalletTransactions_WalletId`) | ❌ | `.../20261024103202_InitialWalletSchema.cs:400`; nhưng `ListTransactionsQuery.cs:27` lọc theo `UserId` |
| Class dư / code chết | **2** (`AppDbContext : WalletDbContext`; package `MediatR.Contracts` không dùng) | ⚠️ | `Wallet.Infrastructure/Persistence/WalletDbContext.cs:37`; `Wallet.Domain/Wallet.Domain.csproj:10` |

**Quy tắc rút ra:**
- **Không tin tài liệu cũ**: các bản trước đây từng ghi sai tên solution (`WalletService.sln`), sai tên rpc (`GetBalance`/`DeductBalance`), sai tên bảng (`Transactions`), sai version MassTransit (`8.4.1`), khẳng định “có test” và “có publish event”. Bản này đã sửa tất cả; nếu thấy lại các chi tiết đó ở đâu, đó là tài liệu chưa cập nhật.
- **Không coi `GET /health` là bằng chứng service hoạt động**: `/health` trả `Healthy` vô điều kiện (`Wallet.Api/Program.cs:66`) và auto-migrate nuốt lỗi (`:49-64`).
- **Mọi tài liệu khi sửa phải có banner trạng thái** ngay dưới H1: `> **Trạng thái triển khai (2026-10-24):** …` + `> **Nguồn sự thật:** code (file:line)` + `> **Cập nhật lần cuối:** 2026-10-24.`
- **Mọi bảng liệt kê tính năng/rpc/event/bảng dữ liệu phải có cột `Trạng thái` (✅/⚠️/❌) và cột `Bằng chứng (file:line)`** — không có ngoại lệ.
