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
   - `x-user-role`: Vai trò đã được Gateway xác thực (`BUYER`, `SELLER`, `ADMIN`, `SUPPORT_STAFF`); service từ chối request thiếu identity/role.
   - `x-client-ip`: Địa chỉ IP gốc của client cho yêu cầu tạo URL VNPay.
   - Khi `x-user-id` có mặt trong gRPC Metadata, Service ưu tiên sử dụng giá trị này thay cho `request.UserId` để bảo mật.
2. **Định Dạng Tiền Tệ**:
   - `balance`, `amount`, `balance_after` luôn ở dạng chuỗi decimal có đúng 2 chữ số thập phân theo chuẩn InvariantCulture (ví dụ: `"500000.00"`).
   - Đơn vị tiền tệ mặc định là `"VND"`.

---

## 4. WALLET OPERATIONS NOT REPRESENTED IN THE LEGACY PROTO

The CSV-defined wallet workflows that need request metadata or return values not present in the legacy protobuf messages are exposed as HTTP endpoints on Wallet.Api. The Gateway must proxy these paths and set trusted `x-user-id`, `x-user-role`, and (for top-ups) `x-client-ip` headers after validating the caller; it must strip client-supplied identity headers.

| Method and path | Access | Purpose |
|:---|:---|:---|
| `GET /api/v1/wallets/{userId}/balance` | BUYER / SELLER (self), ADMIN | Balance, locked balance, and available balance |
| `GET /api/v1/wallets/{userId}/transactions` | BUYER / SELLER (self), ADMIN | Filtered and paginated ledger history (`type`, `fromDate`, `toDate`, `page`, `pageSize`) |
| `POST /api/v1/wallet/topups` | BUYER / SELLER | Initiate a VNPay top-up |
| `GET /api/v1/wallet/vnpay/ipn` | VNPay callback; signature required | Idempotently process the VNPay IPN |
| `POST /api/v1/wallet/payments` | BUYER / SELLER | Initiate a configured direct-gateway payment |
| `POST /api/v1/wallet/withdrawals` | SELLER | Lock available funds and submit a withdrawal request |
| `POST /api/v1/admin/withdrawals/{withdrawalId}` | ADMIN / SUPPORT_STAFF | Approve or reject a pending withdrawal |
| `POST /api/v1/admin/wallets/{userId}/adjustments` | ADMIN | Credit/debit with an audit record |

These endpoints are additive and do not change the shared protobuf schema. The VNPay IPN endpoint is the only unauthenticated route; it validates VNPay's HMAC-SHA512 signature before processing. Direct gateways use a configured HTTPS URL and HMAC-SHA512 signature over `orderId|amount|currency`; each provider must accept those query parameters.

The legacy gRPC `Deposit` RPC now creates a pending VNPay top-up and returns the payment URL in `WalletTransaction.note`; it never credits the wallet before a signed IPN. The legacy `Withdraw` RPC returns `UNIMPLEMENTED` because its protobuf request cannot carry the required bank details or approval state; use the withdrawal HTTP endpoints instead.

Configure VNPay under `VnPay` and direct providers under `PaymentGateways:{gatewayCode}`. Never use the example placeholder secrets in a deployed environment.
