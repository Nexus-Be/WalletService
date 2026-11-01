---
name: service-api-doc
description: Pre-code API documentation standard for WalletService (port 8008). Mandates drafting a service-level technical API doc before coding, referencing existing domain entities, enums, MediatR Command/Query contracts, and error patterns. Governs classic CQRS contracts, pure DTOs, and RFC 9110 error matrices inside WalletService.
---

# WalletService API Pre-Code Documentation Skill

> **Scope:** WalletService repository only (`src/Services/Wallet/`). 100% Inward-Only. Never reference the root workspace or sibling services.
> **Kiến trúc đóng băng (Architecture Freeze):** WalletService là service duy nhất sử dụng classic CQRS qua MediatR. Mọi use case là Command/Query + Handler. Controllers chỉ inject `ISender`. Cấm tạo `I{Feature}Service` facade.

This skill governs the **mandatory pre-code documentation lifecycle** within WalletService. Before any C# code is written or modified for a REST endpoint, the developer or AI agent must create a technical API specification inside `docs/api/`.

---

## 1. Ranh giới Tài liệu WalletService

* **Vị trí lưu trữ:** `docs/api/{Area}/{HTTP_METHOD}_{ENDPOINT_SLUG}.md` (ví dụ `docs/api/Wallets/POST_WALLETS_WITHDRAWALS.md`).
* **Thời điểm lập:** **BẮT BUỘC TRƯỚC KHI VIẾT CODE (Pre-Code)**.
* **Định dạng dữ liệu trả về:** **Pure DTO** hoặc `PagedData<T>` (tuyệt đối không bọc qua `ApiResponse<T>`).
* **Rào chắn Inward-Only:** Chỉ trỏ tới tài nguyên nội bộ của WalletService (`Wallet.Domain`, `Wallet.Application`, `Wallet.Infrastructure`, `Wallet.Api`). Không tham chiếu tới `docs/api/` ở Root.

---

## 2. Quy trình 3 Bước Tra cứu Source of Truth Trước Khi Lập Doc

### Bước 1: Tra cứu Thực thể Domain (`Wallet.Domain/Entities/`)
- Mở `Wallet.Domain/Entities/` (ví dụ `Wallet.cs`, `WalletTransaction.cs`, `WalletTopup.cs`, `BankAccount.cs`).
- Ràng buộc bất biến: Số dư không âm (`Balance >= 0`), độ chính xác tiền tệ `HasPrecision(18, 2)`.

### Bước 2: Tra cứu Bảng mã Enum (`Wallet.Domain/Enums/`)
- Mở `Wallet.Domain/Enums/` (ví dụ `TransactionType.cs`, `TransactionStatus.cs`, `WithdrawalStatus.cs`).
- Ghi nhận số nguyên DB (`0`, `1`, `2`...) và chuỗi PascalCase API (`Deposit`, `Withdrawal`, `Payment`, `Success`, `Pending`).
- Điền bảng ánh xạ vào Mục 4.1 của doc.

### Bước 3: Tra cứu Mẫu CQRS & Xử lý Lỗi Có Sẵn (`Wallet.Application/Features/`)
- Rà soát các use case có sẵn tại `Wallet.Application/Features/{Area}/Commands|Queries/`.
- Kế thừa quy ước đặt tên: `{UseCase}Command`, `{UseCase}Query`, `{UseCase}Handler`, `{UseCase}Response`.
- Kế thừa mã lỗi tài chính đã có (`WALLET_NOT_FOUND`, `INSUFFICIENT_BALANCE`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`).

---

## 3. Các Rào Chắn Bất Biến tại WalletService

1. **Classic CQRS với MediatR:** Mọi use case bắt buộc đặt tại `Wallet.Application/Features/{Area}/{Commands|Queries}/{UseCase}/`. Handler inject trực tiếp `IWalletDbContext`. Controller chỉ inject `ISender`.
2. **Không số dư âm (Negative Balance Invariant):** Mọi thao tác rút, chuyển hoặc trừ tiền thanh toán bắt buộc kiểm tra `Balance >= Amount`.
3. **Pure DTO:** Controller tại WalletService trả về DTO thuần, cấm thêm `ResultFilter` hay bọc `ApiResponse<T>`.
4. **Plural Route:** `/api/v1/wallets`, `/api/v1/wallets/transactions`, `/api/v1/wallets/withdrawals`, `/api/v1/admin/wallets/...`.
5. **Mã lỗi & Message:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (cấm tiền tố `ERR_`).

---

## 4. Quy trình Thực hiện (Workflow)

1. Sao chép `docs/api/SERVICE_API_DOC_TEMPLATE.md` vào `docs/api/{Area}/{HTTP_METHOD}_{ENDPOINT_SLUG}.md`.
2. Điền đầy đủ 5 phần của template dựa trên kết quả tra cứu Source of Truth.
3. Hoàn thiện mục 5.1 (Pre-Code Plan) mô tả luồng Command/Query Handler và Transaction DB.
4. Triển khai code C# tuân thủ 100% hợp đồng đã đặc tả trong doc.
5. Chạy `./scripts/verify-wallet-architecture.sh`, `dotnet build Wallet.sln -c Release` và `dotnet test Wallet.sln -c Release` đạt 100% pass, tích checklist và chuyển trạng thái doc sang `✅ VERIFIED`.
