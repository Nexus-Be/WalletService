# 02 — HỢP ĐỒNG GIAO TIẾP GRPC (WALLET SERVICE GRPC CONTRACTS)

Dịch vụ gRPC của WalletService được định nghĩa trong hợp đồng duy nhất `Contracts/shop.proto` dưới service `WalletApi`:

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

---

## 3. QUY ƯỚC HEADER & ĐỊNH DANH

1. **Header Truyền Xuống Từ Gateway**:
   - `x-user-id`: Mã UUID định danh của người dùng đã được giải mã từ JWT token tại Gateway.
   - Khi `x-user-id` có mặt trong gRPC Metadata, Service ưu tiên sử dụng giá trị này thay cho `request.UserId` để bảo mật.
2. **Định Dạng Tiền Tệ**:
   - `balance`, `amount`, `balance_after` luôn ở dạng chuỗi decimal có đúng 2 chữ số thập phân theo chuẩn InvariantCulture (ví dụ: `"500000.00"`).
   - Đơn vị tiền tệ mặc định là `"VND"`.
