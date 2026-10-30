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

> **Trạng thái triển khai (2026-10-30):** ✅ ĐÃ CHUẨN HÓA — 100% RESTful API (`WalletsController`, `AdminWalletsController`), Feature Co-location, cấm gRPC, bãi bỏ hoàn toàn RabbitMQ, MassTransit, Saga và Outbox/Inbox tables.
> **Nguồn sự thật:** code (`Wallet.sln`, `Wallet.Api/Controllers/WalletsController.cs`, `Wallet.Api/Controllers/AdminWalletsController.cs`).
> **Cập nhật lần cuối:** 2026-10-30.

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
| 2 | `specs/01_CLEAN_ARCHITECTURE_LAYERS.md` | 4 tầng Clean Architecture, No-Repository pattern over EF Core, Feature Co-location | ✅ | `specs/01_CLEAN_ARCHITECTURE_LAYERS.md:1` |
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
   - Toàn bộ giao tiếp đồng bộ giữa API Gateway và WalletService (hoặc giữa các microservices) bắt buộc sử dụng chuẩn **RESTful Web API** (JSON qua HTTP/1.1 hoặc HTTP/2).
   - Tuyệt đối cấm gRPC, Protobuf, RabbitMQ và MassTransit.
5. **FEATURE-DRIVEN CO-LOCATION**:
   - Tầng Application: Toàn bộ thành phần của một tính năng đặt tại `Wallet.Application/Services/{Feature}/` gồm `I{Feature}Service.cs`, `{Feature}Service.cs`, `{Feature}Requests.cs`, `{Feature}Responses.cs`, `{Feature}Dtos.cs`.
   - Tầng Api: Ghép cặp 1-to-1 với thin controller tại `Wallet.Api/Controllers/{Feature}Controller.cs`, chỉ inject duy nhất `I{Feature}Service`.
6. **CẤM COMMIT SECRET**: Mọi secret key, DB password phải được nạp qua biến môi trường.

---

## 💬 NGUYÊN TẮC 5: QUY CHUẨN THÔNG BÁO LỖI TOÀN HỆ THỐNG

1. **Ngôn ngữ (100% English)**: Toàn bộ thông báo lỗi viết bằng tiếng Anh. Tuyệt đối không dùng tiếng Việt trong chuỗi trả về client.
2. **Quy cách viết hoa (Sentence case)**: Viết hoa duy nhất chữ cái đầu câu: `"Insufficient balance to complete the transaction."`
3. **Mã lỗi (SCREAMING_SNAKE_CASE)**: `WALLET_NOT_FOUND`, `INSUFFICIENT_BALANCE`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`.

---

## 🎯 NGUYÊN TẮC 6: ĐIỀU KIỆN HOÀN THÀNH NHIỆM VỤ (DEFINITION OF DONE)

Một task tại WalletService chỉ hoàn tất khi:
* Đã lập Plan chi tiết trước khi thực hiện.
* Không chứa bất kỳ liên kết trỏ ngược ra ngoài WalletService.
* `dotnet build Wallet.sln -c Release` thành công 100% (0 error).
* `dotnet test Wallet.sln -c Release` vượt qua 100% các bài test.
* Trạng thái Git sạch, không commit file rác (`bin/`, `obj/`, `.vs/`, `.idea/`).
