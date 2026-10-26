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
| `GET /api/v1/wallets/me` | BUYER / SELLER / ADMIN | Current caller's balance, locked balance, and available balance |
| `GET /api/v1/wallets/transactions` | BUYER / SELLER / ADMIN | Current caller's filtered/paginated ledger (`type`, `fromDate`, `toDate`, `page`, `limit`) |
| `POST /api/v1/wallets/topups/vnpay` | BUYER / SELLER | Initiate a VNPay top-up (`amount`, `bankCode`, `returnUrl`) |
| `POST /api/v1/wallets/vnpay/ipn` | VNPay callback; signature required | Idempotently process the VNPay IPN body |
| `POST /api/v1/wallets/checkout/direct` | BUYER / SELLER | Initiate direct-gateway payment (`orderId`, `amount`, `gateway`, optional `bankCode`, `returnUrl`) |
| `POST /api/v1/wallets/withdrawals` | SELLER | Lock available funds and submit a withdrawal (`bankCode`, `accountNumber`, `accountHolderName`) |
| `PATCH /api/v1/admin/wallets/withdrawals/{id}` | ADMIN / SUPPORT_STAFF | Approve or reject a pending withdrawal |
| `POST /api/v1/admin/wallets/{walletId}/adjustments` | ADMIN | Credit/debit with an audit record |

Successful HTTP responses use the shared `ApiResponse<T>` or `ApiResponse<PagedData<T>>` envelope, except for VNPay IPN, which returns the provider's `{ "RspCode": "...", "Message": "..." }` response. Errors use the shared RFC 9110-style error envelope.

These endpoints are additive and do not change the shared protobuf schema. The VNPay IPN endpoint is the only unauthenticated route; it validates VNPay's HMAC-SHA512 signature before processing. Direct gateways use a configured HTTPS URL and HMAC-SHA512 signature over the order, amount, currency, and optional bank/return URL parameters; each provider must accept those query parameters.

Withdrawal requests follow the agreed endpoint contract's zero-fee response (`fee: 0`); the full requested amount is locked and later deducted on approval.

The legacy gRPC `Deposit` RPC now creates a pending VNPay top-up and returns the payment URL in `WalletTransaction.note`; it never credits the wallet before a signed IPN. The legacy `Withdraw` RPC returns `UNIMPLEMENTED` because its protobuf request cannot carry the required bank details or approval state; use the withdrawal HTTP endpoints instead.

Configure VNPay under `VnPay` and direct providers under `PaymentGateways:{gatewayCode}`. Never use the example placeholder secrets in a deployed environment.
