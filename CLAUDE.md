<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **WalletService** (429 symbols, 686 relationships, 4 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? Bootstrap with `npx`, `bunx`, or `pnpm dlx` — e.g. `bunx gitnexus@latest analyze` (npm 11 npx crash; #1939).

## Always Do

- **MUST run impact before editing.** Use `impact({target: "symbolName", direction: "upstream"})` or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .`; report callers, processes, and risk. Never substitute grep for graph analysis.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` (MCP) or `node .gitnexus/run.cjs detect-changes --scope all --repo .` (CLI fallback). `partial: true` or `truncated: true` is not a clean check — a zero means unseen, not unaffected; re-run it. For regression review: `detect_changes({scope: "compare", base_ref: "main"})` or `node .gitnexus/run.cjs detect-changes --scope compare --base-ref "main" --repo .`.
- MUST warn on HIGH/CRITICAL `risk` pre-edit; never use `riskSharedAxes` to waive a HIGH/CRITICAL `risk` warning. Compare File/symbol: MCP File omits axes; Graph-RAG expands File.
- **MUST treat `risk: UNKNOWN` as unresolved, not as low.** An empty caller set is not evidence the symbol is unused — it can also mean the callers are not resolvable by the index (plain-object property access, dynamic dispatch, cross-language calls). `impact` pairs `UNKNOWN` with a `riskNote` saying so. Confirm with a text search before treating the symbol as safe to change or delete; do not proceed on the strength of a zero.
- **MUST use `query({search_query: "concept"})` for concepts/flows, `context({name: "symbolName"})` for a named symbol, or `impact` for blast radius, on read-only callers, dependencies, imports, or execution flow.** Graph first; text search only for empty/`UNKNOWN`/literals.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method before MCP/CLI impact analysis.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis, and never read `UNKNOWN` as an all-clear — it means the walk could not answer, which is the one verdict that requires confirming by other means.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit before MCP/CLI graph change analysis.

## Resources

| Resource | Use for |
| --- | --- |
| `gitnexus://repo/WalletService/context` | Codebase overview, check index freshness |
| `gitnexus://repo/WalletService/clusters` | All functional areas |
| `gitnexus://repo/WalletService/processes` | All execution flows |
| `gitnexus://repo/WalletService/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
| --- | --- |
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus-cli/SKILL.md` |

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
4. **CẤM SỬ DỤNG gRPC CHO CÁC DỊCH VỤ ĐỒNG BỘ - BẮT BUỘC DÙNG 100% RESTFUL API**:
   - Toàn bộ giao tiếp đồng bộ giữa API Gateway và WalletService (hoặc giữa các microservices) bắt buộc phải sử dụng chuẩn **RESTful Web API** (JSON qua HTTP/1.1 hoặc HTTP/2).
   - Tuyệt đối **CẤM** triển khai hoặc sử dụng gRPC Server/Client cho các dịch vụ đồng bộ trong toàn bộ hệ thống.
   - Toàn bộ các hợp đồng Protobuf (`.proto`) trôi nổi đã được loại bỏ hoàn toàn khỏi WalletService. Mọi API expose qua REST Controller (`WalletsController.cs`).
5. **CẤM TIN HEADER NHƯ MỘT CƠ CHẾ BẢO MẬT**: `x-user-id` chỉ là metadata Gateway chuyển xuống; không được dùng nó thay cho xác thực.
   — *Hiện trạng:* ❌ service **không có authentication** nhưng vẫn ưu tiên `x-user-id` (`Wallet.Api/Services/WalletGrpcService.cs:28,48,68,96,124`). Không được viết tài liệu mô tả đây là “bảo mật”.
6. **CẤM MÔ TẢ TÍNH NĂNG CHƯA CÓ LÀ ĐÃ CÓ**: VNPay topup/IPN, rút tiền qua ngân hàng (`WithdrawalRequest`), `AuditLog` **chưa được triển khai** (0 đường ghi). Xem `specs/04_DATA_PERSISTENCE.md` mục 3.
7. **CẤM COMMIT SECRET**: `Wallet.Api/appsettings.json` hiện **đang được track** và chứa mật khẩu DB/RabbitMQ (`:10`,`:16`). Mọi thay đổi tiếp theo không được thêm secret mới; việc gỡ file này khỏi git là P0.
8. **CẤM TỰ Ý SỬA ĐỔI BIẾN MÔI TRƯỜNG, SECRET VÀ KEY BÍ MẬT (MANDATORY HUMAN-IN-THE-LOOP FOR SECRETS)**:
   - **Tuyệt đối CẤM mọi AI Coding Agent tự ý sửa đổi, thay thế, xóa hoặc ghi đè các biến môi trường (`.env`, `.env.*`), Connection Strings, API Keys, JWT Secrets, chứng chỉ hoặc bất kỳ thông tin bí mật (credentials/secrets) nào trong toàn bộ hệ thống.**
   - **Bắt buộc phải xin ý kiến và nhận được sự đồng ý rõ ràng (explicit approval) từ người dùng trước khi thực hiện bất kỳ thay đổi nào liên quan đến biến môi trường hoặc tài khoản bí mật.**
   - Khi phát hiện cấu hình môi trường, connection string hoặc secret key có vấn đề hoặc cần cập nhật, Agent **CHỈ ĐƯỢC PHÉP báo cáo hiện trạng, đề xuất phương án và chờ người dùng xác nhận**, tuyệt đối không tự ý can thiệp.

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
