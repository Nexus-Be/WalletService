# WalletService — [HTTP_METHOD] [ROUTE]

> **Feature / Use Case:** {UseCaseName}
> **Trạng thái:** ⏳ DRAFT (Pre-Code Plan) | 🛠 IN_PROGRESS | ✅ VERIFIED
> **Ngày tạo:** YYYY-MM-DD
> **Tác giả:** {Author / AI Agent}

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET` | `POST` | `PATCH`
- **Route Endpoint:** `/api/v1/wallets/...` | `/api/v1/admin/wallets/...`
- **Quyền hạn (RBAC Policy):** `User` | `Buyer` | `Seller` | `StaffOrAdmin` | `Admin`
- **Quy tắc Sở hữu (Resource Ownership):** `currentUserId == targetUserId` hoặc Admin/Staff bypass.
- **Controller:** `Wallet.Api/Controllers/Public/WalletsController.cs` (hoặc `AdminWalletsController.cs`)
- **CQRS Handler:** `Wallet.Application/Features/{Area}/{Commands|Queries}/{UseCase}/{UseCase}Handler.cs` (chỉ inject `ISender` tại Controller)

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `transactionId` | Route | `Guid` | Có | UUIDv4 format | Khóa chính giao dịch ví |
| `page` | Query | `int` | Không | Min: 1, Default: 1 | Tham số phân trang chuẩn |
| `pageSize` | Query | `int` | Không | Range: 1 - 100, Default: 20 | Giới hạn tối đa 100 bản ghi |

### 2.2 Request Body / Command Record
- **Vị trí file:** `Wallet.Application/Features/{Area}/Commands/{UseCase}/{UseCase}Command.cs`
- **Tên C# Record:** `{UseCase}Command`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record {UseCase}Command(
    [Range(10000, 100000000, ErrorMessage = "Amount must be between 10,000 and 100,000,000 VND.")]
    decimal Amount,

    [Required(ErrorMessage = "Bank code is required.")]
    string BankCode
) : IRequest<{UseCase}Response>
{
    // Server-populated properties ignored in client JSON payload
    [JsonIgnore]
    public Guid UserId { get; init; }
}
```
- **JSON Request Body mẫu (camelCase):**
```json
{
  "amount": 500000.00,
  "bankCode": "NCB"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** WalletService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ `ApiResponse<T>` sẽ do API Gateway tự động đóng gói tại biên.

- **Vị trí file:** `Wallet.Application/Features/{Area}/Commands/{UseCase}/{UseCase}Response.cs`
- **Tên C# Record:** `{UseCase}Response` (hoặc `PagedData<{Item}Dto>`)
- **Mã HTTP thành công:** `200 OK` | `201 Created` | `204 NoContent`
- **Khai báo C# Record:**
```csharp
public sealed record {UseCase}Response(
    Guid TransactionId,
    decimal Amount,
    decimal BalanceAfter,
    string Status, // PascalCase enum string
    DateTimeOffset CreatedAt
);
```
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "transactionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 500000.00,
  "balanceAfter": 1500000.00,
  "status": "Success",
  "createdAt": "2026-11-01T14:30:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)
> **Quy chuẩn:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (không dùng tiền tố `ERR_`).

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `One or more validation errors occurred.` | Vi phạm DataAnnotations trên command |
| `400 Bad Request` | `INSUFFICIENT_BALANCE` | `Insufficient balance to complete the transaction.` | Số dư ví nhỏ hơn số tiền cần trừ |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Thiếu hoặc token không hợp lệ |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access or modify this resource.` | Người dùng không sở hữu ví |
| `404 Not Found` | `WALLET_NOT_FOUND` | `Wallet account not found for the specified user.` | Chưa khởi tạo ví cho user |
| `500 Internal Error` | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred while processing the request.` | Ngoại lệ chưa được xử lý |

### 4.1 Bảng Ánh xạ Enum liên quan (Nếu có)
| Tên Enum C# | Giá trị DB (Integer) | Giá trị API (PascalCase) | Ý nghĩa nghiệp vụ |
| :--- | :---: | :--- | :--- |
| `TransactionType.Topup` | `0` | `"Topup"` | Nạp tiền vào ví |
| `TransactionType.Withdrawal` | `1` | `"Withdrawal"` | Rút tiền khỏi ví |
| `TransactionStatus.Success` | `0` | `"Success"` | Giao dịch thành công |
| `TransactionStatus.Pending` | `1` | `"Pending"` | Giao dịch đang chờ xử lý |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)
### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Controller inject `ISender`, lấy `userId` từ `HttpContext.GetUserId()` gán vào Command qua `with { UserId = ... }`.
2. **Bước 2:** Command đi qua `ValidationBehavior<TRequest, TResponse>` pipeline để validate tự động.
3. **Bước 3:** Handler tải ví từ `_dbContext.Wallets`, kiểm tra số dư `Balance >= Amount`.
4. **Bước 4:** Tạo bản ghi `WalletTransaction` và cập nhật `Balance`.
5. **Bước 5:** Lưu vào DB trong transaction an toàn qua `_dbContext.SaveChangesAsync()`.
6. **Bước 6:** Handler trả về `Response` record, Controller trả về `Ok(response)` (Pure DTO).

### 5.2 Checklist hoàn thành
- [ ] **Pre-Code:** Đã đối soát code có sẵn trong WalletService để tái sử dụng Command/Query/Enum.
- [ ] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/{Area}/{METHOD}_{ENDPOINT}.md`.
- [ ] **CQRS Structure:** Command/Query, Handler, Response đặt đúng tại `Wallet.Application/Features/{Area}/{Commands|Queries}/{UseCase}/`.
- [ ] **Api:** Action nằm trong `WalletsController` hoặc `AdminWalletsController`, chỉ inject `ISender`.
- [ ] **Verification Script:** Chạy `./scripts/verify-wallet-architecture.sh` báo `OK: Wallet CQRS architecture is intact`.
- [ ] **Tests:** Đã viết unit/integration tests bao phủ happy path và các mã lỗi trong Error Matrix.
- [ ] **Build:** `dotnet build Wallet.sln -c Release` và `dotnet test Wallet.sln -c Release` vượt qua 100% với 0 lỗi.
