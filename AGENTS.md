<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **WalletService** (429 symbols, 686 relationships, 4 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root.

## Always Do

- **MUST run impact before editing.** Use `impact({target: "symbolName", direction: "upstream"})` or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .`.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` or `node .gitnexus/run.cjs detect-changes --scope all --repo .`.
- **MUST warn on HIGH/CRITICAL `risk` pre-edit.**
- **MUST treat `risk: UNKNOWN` as unresolved, not low.**
- **NEVER** edit before impact analysis · **NEVER** rename with find-and-replace · **NEVER** commit before detect_changes.

<!-- gitnexus:end -->

---

# QUY CHUẨN VẬN HÀNH DÀNH CHO AI CODING AGENTS (WALLET SERVICE)

> **Trạng thái triển khai (2026-10-31):** ✅ ĐÃ CHUẨN HÓA — 100% RESTful API, **classic CQRS qua MediatR** (11 use case: 8 Command, 3 Query; `EnsureWalletCommand` được query gọi nội bộ), controller chỉ inject `ISender`, cấm gRPC, bãi bỏ hoàn toàn RabbitMQ, MassTransit, Saga và Outbox/Inbox tables. WalletService là **ngoại lệ duy nhất** của lệnh cấm MediatR toàn hệ thống.
> **Nguồn sự thật:** code (`Wallet.sln`, `Wallet.Api/Controllers/Public/`, `Wallet.Application/Features/`).
> **Cập nhật lần cuối:** 2026-10-31.

---

## 0. KIẾN TRÚC ĐÓNG BĂNG — KIỂM TRA TRƯỚC KHI SỬA (ARCHITECTURE FREEZE)

WalletService là **service duy nhất** dùng MediatR/CQRS. Cấu trúc này từng bị đảo ngược bởi agent khác, nên được đóng băng.

**Bất biến:**
* Đúng 11 use case tại `Wallet.Application/Features/{Area}/Commands|Queries/{UseCase}/`, đúng 11 file `*Handler.cs`.
* Đúng 2 controller: `Controllers/Public/WalletsController.cs`, `Controllers/Public/AdminWalletsController.cs`. `CheckoutDirect` đã gộp vào `WalletsController`. Mỗi controller chỉ inject `ISender` và `ILogger`.
* `MediatR 12.4.1` trong `Wallet.Application.csproj`; `AddMediatR` + `ValidationBehavior<,>` trong `DependencyInjection.cs`.
* Handler inject trực tiếp `IWalletDbContext`.

**Cấm tuyệt đối:**
* Tạo lại `Wallet.Application/Services/{Feature}/` hoặc bất kỳ `I{Feature}Service` / `{Feature}Service` facade.
* Tách controller theo tài nguyên (`TopupsController`, `TransfersController`, `VnPayController`, `WithdrawalsController`...). Giữ đúng 2 controller.
* Xóa `Features/`, gỡ package MediatR, hoặc đổi controller sang inject service.
* Đổi route, mã HTTP, JSON contract khi chưa được duyệt.

**Kiểm tra bắt buộc trước khi commit:**
```bash
./scripts/verify-wallet-architecture.sh   # phải in "OK: Wallet CQRS architecture is intact"
dotnet build Wallet.sln -c Release
dotnet test  Wallet.sln -c Release
```

Nếu một tool/agent đề xuất feature service, controller theo tài nguyên, hoặc bỏ MediatR: **dừng lại và hỏi Sếp**, không tự áp dụng.

---

## Endpoint Workflow

Before adding or changing any REST endpoint, follow the local checklist at `.agents/skills/new-feature-endpoint/SKILL.md`. It covers contract, persistence, Application, Api, tests, documentation handoff, and verification. This repository is Inward-Only, so the checklist is self-contained.

---

## 🛑 NGUYÊN TẮC 1: BẮT BUỘC LẬP KẾ HOẠCH TRƯỚC KHI HÀNH ĐỘNG (PLAN-FIRST RULE)

Mọi AI Agent khi nhận nhiệm vụ kỹ thuật tại WalletService **BẮT BUỘC PHẢI LẬP VÀ TRÌNH BÀY PLAN** trước khi thực hiện bất kỳ hành động sửa đổi mã nguồn, thêm/xóa file, refactor, tạo migration database hay thay đổi cấu hình nào.

Một Plan chuẩn bắt buộc phải có:
1. **Mục tiêu (Objective)**: Tóm tắt chính xác điều cần làm.
2. **Context & Files cần đọc**: Liệt kê các file nội bộ của WalletService theo ma trận đọc file tại Nguyên tắc 3.
3. **Phân tích rủi ro & Phạm vi ảnh hưởng (Impact Analysis)**: Dùng GitNexus `impact`.
4. **Checklist các bước thực hiện (Step-by-Step Implementation)**.
5. **Kế hoạch kiểm chứng (Verification)**: `dotnet build Wallet.sln -c Release` và `dotnet test Wallet.sln -c Release`.

---

## 🧭 NGUYÊN TẮC 2: QUY TẮC PHẠM VI NỘI BỘ (INWARD-ONLY / SELF-CONTAINED)

WalletService là một Git Repository độc lập:
* **TUYỆT ĐỐI CHỈ ĐƯỢC trỏ tới các tài nguyên nội bộ của WalletService**.
* **TUYỆT ĐỐI KHÔNG ĐƯỢC trỏ ngược ra ngoài Root** (`../../../docs/...`) hay sang các Service khác (`../../Catalog/...`).
* Toàn bộ tài liệu, hướng dẫn và cấu hình trong repo này phải 100% tự cung tự cấp (Self-contained).

---

## 📚 NGUYÊN TẮC 3: DANH MỤC FILE NỘI BỘ BẮT BUỘC PHẢI ĐỌC TẠI WALLET SERVICE

Khi bắt đầu làm việc tại WalletService, AI Agent **BẮT BUỘC PHẢI ĐỌC** các file nội bộ sau theo thứ tự:

| # | File | Nội dung phải nắm | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---|:---:|:---|
| 1 | `specs/00_AGENT_MISSION_GUARDRAILS.md` | Bounded context số dư ví, nạp/rút/chuyển tiền, thanh toán; port `8008`; DB `5440` (`wallet_db`) | ✅ | `specs/00_AGENT_MISSION_GUARDRAILS.md:1` |
| 2 | `specs/01_CLEAN_ARCHITECTURE_LAYERS.md` | 4 tầng Clean Architecture, classic CQRS/MediatR layout, No-Repository pattern over EF Core | ✅ | `specs/01_CLEAN_ARCHITECTURE_LAYERS.md:1` |
| 3 | `specs/02_API_REST_CONTRACTS.md` | Hợp đồng RESTful API: `WalletsController`, `AdminWalletsController` | ✅ | `specs/02_API_REST_CONTRACTS.md:1` |
| 4 | `specs/03_EVENT_MESSAGING.md` | Trạng thái bãi bỏ RabbitMQ / MassTransit; thanh toán trực tiếp qua REST | ✅ | `specs/03_EVENT_MESSAGING.md:1` |
| 5 | `specs/04_DATA_PERSISTENCE.md` | Schema PostgreSQL: `Wallets`, `WalletTransactions`, `WalletTopups`, `BankAccounts`... | ✅ | `Wallet.Infrastructure/Persistence/WalletDbContext.cs` |
| 6 | `specs/05_DEV_VERIFICATION_CHECKLIST.md` | Lệnh build & test kiểm chứng service | ✅ | `specs/05_DEV_VERIFICATION_CHECKLIST.md:1` |

---

## 🔒 NGUYÊN TẮC 4: RÀO CHẮN BẤT BIẾN TẠI WALLET SERVICE

1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**: Sử dụng trực tiếp `WalletDbContext` và `DbSet<T>` (hoặc interface `IWalletDbContext`) trong tầng Application. Cấm tạo `IWalletRepository`, `ITransactionRepository`, `UnitOfWork`.
2. **TUYỆT ĐỐI KHÔNG ĐỂ SỐ DƯ ÂM (NEGATIVE BALANCE)**: Mọi thao tác rút, chuyển hoặc trừ tiền thanh toán bắt buộc kiểm tra `Balance >= Amount`. Thiếu tiền phải ném ngoại lệ không cho phép giao dịch.
3. **CẤU HÌNH PORT 8008 VÀ DATABASE 5440 LÀ BẤT BIẾN**.
4. **CẤM SỬ DỤNG gRPC VÀ BÃI BỎ MESSAGE BROKER — BẮT BUỘC DÙNG 100% RESTFUL API**:
   - Toàn bộ giao tiếp đồng bộ giữa API Gateway và WalletService (hoặc giữa các microservices) bắt buộc sử dụng chuẩn **RESTful Web API** (JSON qua HTTP/1.1 hoặc HTTP/2) với quy chuẩn 100% số nhiều cho collections: `/api/v1/wallets`, `/api/v1/wallets/transactions`, `/api/v1/wallets/withdrawals`.
   - Tuyệt đối cấm gRPC, Protobuf, RabbitMQ và MassTransit.
5. **CLASSIC CQRS VỚI MEDIATR (BẮT BUỘC — NGOẠI LỆ DUY NHẤT TOÀN HỆ THỐNG)**:
   - Tầng Application: mỗi use case là một cặp Command/Query + Handler đặt tại `Wallet.Application/Features/{Area}/{Commands|Queries}/{UseCase}/` gồm `{UseCase}Command.cs` (hoặc `{UseCase}Query.cs`), `{UseCase}Handler.cs`, và response record.
   - Handler inject trực tiếp `IWalletDbContext`; cấm tạo lại `IWalletService`/`IAdminWalletService` facade hoặc bất kỳ `I{Feature}Service` nào, cấm `IRepository<T>`, `UnitOfWork`.
   - Query chỉ đọc (`AsNoTracking`); Command thay đổi trạng thái và bọc transaction khi đụng số dư hoặc audit log.
   - Tầng Api: thin controller inject duy nhất `ISender`; mỗi action dựng Command/Query và gọi `_sender.Send(...)`. Không query DB, không chứa nhánh nghiệp vụ trong controller.
   - Trường do server điền (`UserId`, `AdminUserId`, `ClientIp`) gắn `[JsonIgnore]` và controller bơm qua `command with { ... }`, để JSON contract không đổi.
   - Mọi Command/Query đi qua `ValidationBehavior<TRequest,TResponse>` (DataAnnotations). Lỗi validation ném `RequestValidationException` với `errorCode = "VAL_INVALID_INPUT"`.
6. **CẤM COMMIT SECRET**: Mọi secret key, DB password phải được nạp qua biến môi trường.
7. **QUY CHUẨN PHÂN TRANG (PAGINATION GOVERNANCE)**:
   - Toàn bộ thao tác phân trang giao dịch, biến động số dư bắt buộc thực hiện trực tiếp tại Database qua `.Skip((page - 1) * pageSize).Take(pageSize)` và `.CountAsync()`.
   - Đóng gói dữ liệu trả về theo cấu trúc phân trang chuẩn `PagedData<T>` (`items` và metadata `pagination: { pageIndex, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage }`).
   - TUYỆT ĐỐI CẤM trả về danh sách thô lớn không giới hạn để Gateway tự phân trang.
8. **INTERNAL API ĐANG TẠM KHÓA (2026-10-31)**:
   - Chỉ còn `Controllers/Public/` với `WalletsController.cs` và `AdminWalletsController.cs`.
   - `CheckoutDirect` nằm tại `POST /api/v1/wallets/checkout/direct` trong `WalletsController`.
   - `InternalServiceAuthMiddleware` và `InternalServiceAuthHandler` đã comment đăng ký; không yêu cầu `X-Internal-Service-Key`.
9. **QUY CHUẨN BẢO VỆ QUYỀN SỞ HỮU TÀI NGUYÊN (RESOURCE OWNERSHIP GOVERNANCE)**:
   - Thao tác xem số dư (`/api/v1/wallets/me`), giao dịch, nạp/rút tiền bắt buộc đọc `currentUserId` từ `HttpContext.GetUserId()`. Người dùng chỉ được thao tác trên ví của chính mình.
   - Thao tác phê duyệt hoặc từ chối yêu cầu rút tiền bắt buộc kiểm tra quyền `admin` hoặc `staff`.

---

## 💬 NGUYÊN TẮC 5: QUY CHUẨN THÔNG BÁO LỖI TOÀN HỆ THỐNG (MANDATORY ERROR & ENUM RULES)

1. **Ngôn ngữ (100% English)**: Toàn bộ thông báo lỗi viết bằng 100% tiếng Anh. Tuyệt đối CẤM dùng tiếng Việt trong response và runtime exception.
2. **Quy cách viết hoa thông báo lỗi (100% Sentence case)**:
   - Viết hoa DUY NHẤT chữ cái đầu tiên của câu thông báo.
   - Toàn bộ các từ tiếp theo viết thường, TRỪ các từ viết tắt kỹ thuật hoặc tên riêng: `ID`, `OTP`, `JWT`, `URL`, `UUID`, `Email`, `API`, `HTTP`.
   - TUYỆT ĐỐI CẤM viết hoa toàn bộ thông báo lỗi (ví dụ cấm: `"INSUFFICIENT BALANCE"`).
   - Ví dụ đúng: `"Insufficient balance to complete the transaction."`, `"Wallet account not found for the specified user."`
3. **Mã lỗi (100% SCREAMING_SNAKE_CASE)**:
   - Viết hoa toàn bộ và nối bằng gạch dưới: `WALLET_NOT_FOUND`, `INSUFFICIENT_BALANCE`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`.
   - CẤM tiền tố `ERR_`. Tiền tố `VAL_` chỉ dành riêng cho validation.
4. **Quy chuẩn Tuần tự hóa Enum đầu ra REST API (PascalCase Serialization)**:
   - 100% Enum trả về trong JSON envelope phải là chuỗi `PascalCase` (`"Success"`, `"Pending"`, `"TopupVnpay"`), cấm chuỗi in hoa toàn bộ hoặc số nguyên.
5. **Cơ chế Duy trì Rule Bền vững ("Ghi vào Rule")**:
   - Khi Người dùng (Sếp) chỉ thị "ghi vào rule", AI Agent bắt buộc phải lập tức ghi nhận và đồng bộ vào file này, `.agents/AGENTS.md`, root `AGENTS.md` và `.cursor/rules/`.

---

## 🎯 NGUYÊN TẮC 6: ĐIỀU KIỆN HOÀN THÀNH NHIỆM VỤ (DEFINITION OF DONE)

Một task tại WalletService chỉ hoàn tất khi:
* Đã lập Plan chi tiết trước khi thực hiện.
* Không chứa bất kỳ liên kết trỏ ngược ra ngoài WalletService.
* 100% endpoint có Command/Query + Handler riêng; không còn `IWalletService` hoặc `IAdminWalletService`.
* `dotnet build Wallet.sln -c Release` thành công 100% (0 error).
* `dotnet test Wallet.sln -c Release` vượt qua 100% các bài test.
* Trạng thái Git sạch, không commit file rác (`bin/`, `obj/`, `.vs/`, `.idea/`).
