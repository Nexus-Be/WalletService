# 02 — HỢP ĐỒNG GIAO TIẾP GRPC (WALLET SERVICE GRPC CONTRACTS)

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — **5/5 rpc đã implement** (`GetWallet`, `Deposit`, `Withdraw`, `Transfer`, `ListTransactions`) và đã map vào pipeline (`Wallet.Api/Program.cs:47`). Nhưng service **không có authentication** (tin header `x-user-id` không kiểm chứng) và file `Wallet.Api/Contracts/shop.proto` là **bản sao trôi nổi** (md5 `8a7d8698a0607cfb8a41c30a7d7b4492`, byte-identical với bản của Log service).
> **Nguồn sự thật:** code (`Wallet.Api/Services/WalletGrpcService.cs:26-160`, `Wallet.Api/Program.cs:47`), hợp đồng (`Wallet.Api/Contracts/shop.proto:100-106,341-364`).
> **Cập nhật lần cuối:** 2026-10-24.

> **Hiện trạng:** 5/5 rpc có handler thật; rpc nào cũng đọc `context.RequestHeaders.Get("x-user-id")` **trước** `request.UserId` mà **không xác thực** (`WalletGrpcService.cs:28,48,68,96,124`). Không có `AddAuthentication`/`UseAuthentication`/`JwtBearer` trong repo. Trường `currency` của request Deposit/Withdraw bị **bỏ qua** — handler luôn dùng `wallet.Currency` (mặc định `Vnd`).

Dịch vụ gRPC của WalletService được định nghĩa trong `Wallet.Api/Contracts/shop.proto` (364 dòng, 9 `service`, namespace `ShoppingCart.Contracts.V1`) dưới `service WalletApi` (dòng 100-106):

---

## 1. DANH SÁCH CÁC RPC METHODS

```protobuf
service WalletApi {
  // Lấy thông tin ví và số dư khả dụng
  rpc GetWallet(UserRequest) returns (Wallet);

  // Nạp tiền vào ví
  rpc Deposit(WalletDepositRequest) returns (WalletTransaction);

  // Rút tiền từ ví về tài khoản ngân hàng
  rpc Withdraw(WalletWithdrawRequest) returns (WalletTransaction);

  // Chuyển tiền giữa hai ví người dùng
  rpc Transfer(WalletTransferRequest) returns (WalletTransaction);

  // Tra cứu lịch sử biến động số dư có phân trang
  rpc ListTransactions(WalletTransactionsRequest) returns (WalletTransactionPage);
}
```

| RPC | Handler thật | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `GetWallet` | `GetWalletQueryHandler` | ✅ | `Wallet.Api/Services/WalletGrpcService.cs:26`; `Wallet.Application/Features/Queries/GetWallet/GetWalletQuery.cs:12` |
| `Deposit` | `DepositCommandHandler` | ⚠️ CÓ nhưng sai ngữ nghĩa | `WalletGrpcService.cs:46`; `Wallet.Application/Features/Commands/Deposit/DepositCommand.cs:22` — **không qua VNPay**, cộng tiền trực tiếp, tạo giao dịch type `TopupVnpay`, **bỏ qua `request.Currency`** (`:44` dùng `CurrencyCode.Vnd`), và **tự tạo ví mới bằng `Guid.NewGuid()` nếu `user_id` không parse được** (`:33,42`) ⇒ có thể sinh ví mồ côi thay vì trả lỗi. Mọi exception đều bị bọc thành `StatusCode.Internal` (`:59-63`) |
| `Withdraw` | `WithdrawCommandHandler` | ⚠️ CÓ nhưng thiếu nghiệp vụ | `WalletGrpcService.cs:66`; `Wallet.Application/Features/Commands/Withdraw/WithdrawCommand.cs:24` — chỉ trừ số dư + ghi `WalletTransaction` type `WithdrawalDebit`; **không tạo `WithdrawalRequest`**, **không gọi ngân hàng**, `bank_account` chỉ được nhét vào `Note` (`:55`, tiếng Việt), bỏ qua `request.Currency`. Thiếu tiền → `FailedPrecondition` (`WalletGrpcService.cs:79-82`), không có ví → `NotFound` (`:83-86`) |
| `Transfer` | `TransferCommandHandler` | ✅ | `WalletGrpcService.cs:94`; `Wallet.Application/Features/Commands/Transfer/TransferCommand.cs:25` — **tự tạo ví người nhận nếu chưa có** (`:50-58`, kể cả `Guid.NewGuid()` khi `recipient_user_id` không parse được — `:52`). Ghi 2 transaction `AdjustmentDebit`/`AdjustmentCredit` (`:72,:85`) trong **một** `SaveChangesAsync` (`:97`) |
| `ListTransactions` | `ListTransactionsQueryHandler` | ✅ | `WalletGrpcService.cs:122`; `Wallet.Application/Features/Queries/ListTransactions/ListTransactionsQuery.cs:11` — mặc định `page=1`, `pageSize=10`, cap `100` (`:23`), `.AsNoTracking()` (`:26`) |
| *(mapping vào ASP.NET Core)* | `app.MapGrpcService<WalletGrpcService>()` | ✅ | `Wallet.Api/Program.cs:47`; `AddGrpc` `:19`; reflection `:23` |

---

## 2. CÁC MESSAGE CONTRACTS LIÊN QUAN

```protobuf
message Wallet {
  string id = 1;
  string user_id = 2;
  string balance = 3;
  string currency = 4;
  string status = 5;
  string updated_at = 6;
}

message WalletDepositRequest {
  string user_id = 1;
  string amount = 2;
  string currency = 3;
  string payment_method = 4;
}

message WalletWithdrawRequest {
  string user_id = 1;
  string amount = 2;
  string currency = 3;
  string bank_account = 4;
}

message WalletTransferRequest {
  string sender_user_id = 1;
  string recipient_user_id = 2;
  string amount = 3;
  string note = 4;
}

message WalletTransaction {
  string id = 1;
  string wallet_id = 2;
  string user_id = 3;
  string type = 4;
  string amount = 5;
  string currency = 6;
  string balance_after = 7;
  string status = 8;
  string note = 9;
  string created_at = 10;
}

message WalletTransactionsRequest {
  string user_id = 1;
  int32 page = 2;
  int32 page_size = 3;
  string type = 4;
}

message WalletTransactionPage {
  repeated WalletTransaction items = 1;
  int32 total = 2;
}
```

| Message | Khai báo tại | Được dùng bởi | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---|:---:|:---|
| `Wallet` | `shop.proto:341` | `GetWallet` | ✅ | `Wallet.Api/Services/WalletGrpcService.cs:26-44` |
| `WalletDepositRequest` | `shop.proto:345` | `Deposit` | ⚠️ | `WalletGrpcService.cs:46-64`; `currency` bị bỏ qua, `payment_method` chỉ vào `Note` (tiếng Việt) |
| `WalletWithdrawRequest` | `shop.proto:348` | `Withdraw` | ⚠️ | `WalletGrpcService.cs:66-92`; `bank_account` chỉ vào `Note` |
| `WalletTransferRequest` | `shop.proto:351` | `Transfer` | ✅ | `WalletGrpcService.cs:94-120` |
| `WalletTransaction` | `shop.proto:354` | `Deposit`/`Withdraw`/`Transfer` (kết quả) | ✅ | `WalletGrpcService.cs:143-158` (`MapTransaction`), gọi tại `:57,77,105,137` |
| `WalletTransactionsRequest` | `shop.proto:359` | `ListTransactions` | ⚠️ CÓ nhưng chậm | `WalletGrpcService.cs:122-141`; `type` **có** được lọc (`ListTransactionsQuery.cs:29-32`), nhưng điều kiện chính `t.UserId == request.UserId` (`:27`) **không có index** — bảng chỉ có `IX_WalletTransactions_WalletId` |
| `WalletTransactionPage` | `shop.proto:362` | `ListTransactions` | ✅ | `WalletGrpcService.cs:130-140`; `total = CountAsync` trước phân trang (`ListTransactionsQuery.cs:34`) |

---

## 3. QUY ƯỚC HEADER & ĐỊNH DANH

| Quy ước | Thực tế trong code | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Gateway truyền `x-user-id` (UUID giải mã từ JWT) xuống gRPC metadata | Có đọc header này ở cả 5 rpc | ⚠️ | `Wallet.Api/Services/WalletGrpcService.cs:28,48,68,96,124` |
| “Service ưu tiên `x-user-id` thay cho `request.UserId` **để bảo mật**” | Có ưu tiên header, **nhưng không có bước xác thực nào** — client tự kết nối trực tiếp port 8008 vẫn giả mạo được header hoặc bỏ trống để dùng `request.UserId` | ❌ Không phải bảo mật | Không có `AddAuthentication`/`JwtBearer`/`UseAuthentication` trong repo; `Program.cs` không có auth middleware |
| Định dạng tiền: chuỗi decimal 2 chữ số, `InvariantCulture` | Đúng | ✅ | `WalletGrpcService.cs:39` (`balance`), `:151,153` (`amount`, `balance_after`) |
| Đơn vị tiền tệ mặc định `VND` | Ví mới luôn tạo với `CurrencyCode.Vnd` | ✅ | `Wallet.Application/Features/Commands/Deposit/DepositCommand.cs:44`; `GetWalletQuery.cs:32-42` |

> **Cảnh báo:** đừng mô tả header `x-user-id` là cơ chế bảo mật trong bất kỳ tài liệu nào. Trạng thái thật là **❌ chưa có authentication** — đây là P0 trước khi mở service ra ngoài mạng nội bộ.
