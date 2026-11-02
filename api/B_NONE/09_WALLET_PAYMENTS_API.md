# 09 — TÀI LIỆU API VÍ ĐIỆN TỬ & GIAO DỊCH TIỀN TỆ (WALLET & PAYMENTS API)

> **Dịch vụ phụ trách:** `WalletService (Port 8008 / Gateway Proxy: Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `8 endpoints`  

Tài liệu chuẩn hóa toàn bộ các API quản lý tài chính cá nhân: Tra cứu số dư ví (Khả dụng, Đóng băng đấu giá), Nạp tiền ví qua cổng thanh toán, Yêu cầu rút tiền về ngân hàng, Chuyển tiền nội bộ, Lịch sử biến động số dư và Phê duyệt lệnh rút tiền của Admin.

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `GET` | [`/api/v1/wallets/me`](#1-get-api-v1-wallets-me) | `Buyer` (Yêu cầu JWT Bearer Token) | Lấy thông tin ví và số dư khả dụng/bị khóa của cá nhân |
| 2 | `POST` | [`/api/v1/wallets/topups/vnpay`](#2-post-api-v1-wallets-topups-vnpay) | `Buyer` (Yêu cầu JWT Bearer Token) | Tạo yêu cầu nạp tiền vào ví qua cổng VNPay và lấy URL thanh toán |
| 3 | `POST` | [`/api/v1/wallets/vnpay/ipn`](#3-post-api-v1-wallets-vnpay-ipn) | `Buyer` (Yêu cầu JWT Bearer Token) | Webhook nhận kết quả IPN từ VNPay (Nạp ví hoặc Thanh toán đơn trực tiếp) |
| 4 | `POST` | [`/api/v1/wallets/checkout/direct`](#4-post-api-v1-wallets-checkout-direct) | `Buyer` (Yêu cầu JWT Bearer Token) | Tạo phiên thanh toán trực tiếp qua cổng VNPay cho đơn hàng (bỏ qua số dư ví) |
| 5 | `POST` | [`/api/v1/wallets/withdrawals`](#5-post-api-v1-wallets-withdrawals) | `Seller` | Người bán (Seller) tạo yêu cầu rút tiền từ ví về tài khoản ngân hàng |
| 6 | `PATCH` | [`/api/v1/admin/wallets/withdrawals/{id}`](#6-patch-api-v1-admin-wallets-withdrawals-id-) | `Admin` / `SuperAdmin` | Quản trị viên duyệt hoặc từ chối yêu cầu rút tiền của Seller |
| 7 | `GET` | [`/api/v1/wallets/transactions`](#7-get-api-v1-wallets-transactions) | `Buyer` (Yêu cầu JWT Bearer Token) | Tra cứu lịch sử biến động sổ cái (wallet_transactions) cá nhân |
| 8 | `POST` | [`/api/v1/admin/wallets/{walletId}/adjustments`](#8-post-api-v1-admin-wallets-walletid-adjustments) | `Admin` / `SuperAdmin` | Admin điều chỉnh số dư ví thủ công (Cộng/Trừ tiền kèm kiểm toán) |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-get-api-v1-wallets-me"></a>
### 1. `GET /api/v1/wallets/me`

**Lấy thông tin ví và số dư khả dụng/bị khóa của cá nhân**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/wallets/me`
- **Mã tham chiếu Excel**: `WAL-02`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT>"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Cấu trúc Envelope:** `ApiResponse<T>` (Đối tượng đơn lẻ chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "data": {
      "walletId": "uuid",
      "userId": "uuid",
      "balance": 1500000.0,
      "lockedBalance": 200000.0,
      "availableBalance": 1300000.0,
      "currency": "VND",
      "status": "ACTIVE",
      "updatedAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `WALLET_LOCKED` | Wallet account is locked or frozen. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `WALLET_NOT_FOUND` | Wallet was not found for the user. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="2-post-api-v1-wallets-topups-vnpay"></a>
### 2. `POST /api/v1/wallets/topups/vnpay`

**Tạo yêu cầu nạp tiền vào ví qua cổng VNPay và lấy URL thanh toán**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/wallets/topups/vnpay`
- **Mã tham chiếu Excel**: `WAL-03`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT>"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "amount": 500000,
    "bankCode": "NCB",
    "returnUrl": "https://shop.example.com/wallet/callback"
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `201 Created`
- **Cấu trúc Envelope:** `ApiResponse<T>` (Đối tượng đơn lẻ chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 201,
    "data": {
      "topupId": "uuid",
      "paymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_Amount=50000000...",
      "txnRef": "TOPUP-20261026-1234",
      "expiresIn": 900
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_AMOUNT` | Deposit amount must be at least 10,000 VND. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `WALLET_LOCKED` | Wallet account is locked or frozen. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="3-post-api-v1-wallets-vnpay-ipn"></a>
### 3. `POST /api/v1/wallets/vnpay/ipn`

**Webhook nhận kết quả IPN từ VNPay (Nạp ví hoặc Thanh toán đơn trực tiếp)**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/wallets/vnpay/ipn`
- **Mã tham chiếu Excel**: `WAL-04`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "vnp_TmnCode": "NEXUS001",
    "vnp_Amount": "50000000",
    "vnp_BankCode": "NCB",
    "vnp_BankTranNo": "VNP14072026",
    "vnp_CardType": "ATM",
    "vnp_OrderInfo": "Topup wallet",
    "vnp_PayDate": "20261026103000",
    "vnp_ResponseCode": "00",
    "vnp_TransactionNo": "14072026",
    "vnp_TransactionStatus": "00",
    "vnp_TxnRef": "TOPUP-20261026-1234",
    "vnp_SecureHash": "abcdef123456..."
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Cấu trúc Envelope:** `ApiResponse<T>` (Đối tượng đơn lẻ chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "RspCode": "00",
    "Message": "Confirm success."
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `200` | `VNPAY_INVALID_CHECKSUM` | Invalid checksum hash. | Hiển thị thông báo tương ứng cho người dùng. |
| `200` | `VNPAY_ORDER_NOT_FOUND` | Order not found. | Hiển thị thông báo tương ứng cho người dùng. |
| `200` | `VNPAY_ORDER_ALREADY_CONFIRMED` | Order already confirmed. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="4-post-api-v1-wallets-checkout-direct"></a>
### 4. `POST /api/v1/wallets/checkout/direct`

**Tạo phiên thanh toán trực tiếp qua cổng VNPay cho đơn hàng (bỏ qua số dư ví)**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/wallets/checkout/direct`
- **Mã tham chiếu Excel**: `WAL-05B`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT>"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "orderId": "uuid",
    "amount": 480000,
    "gateway": "VNPAY",
    "bankCode": "NCB",
    "returnUrl": "https://shop.example.com/checkout/callback"
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Cấu trúc Envelope:** `ApiResponse<T>` (Đối tượng đơn lẻ chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "data": {
      "transactionId": "uuid",
      "orderId": "uuid",
      "amount": 480000,
      "paymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_Amount=48000000...",
      "status": "PENDING",
      "createdAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_AMOUNT` | Payment amount must be greater than zero. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ORDER_ALREADY_PAID` | Order has already been paid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `ORDER_NOT_FOUND` | Order was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="5-post-api-v1-wallets-withdrawals"></a>
### 5. `POST /api/v1/wallets/withdrawals`

**Người bán (Seller) tạo yêu cầu rút tiền từ ví về tài khoản ngân hàng**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/wallets/withdrawals`
- **Mã tham chiếu Excel**: `WAL-07`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT>"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "amount": 200000,
    "bankCode": "MB",
    "accountNumber": "0987654321",
    "accountHolderName": "NGUYEN VAN A"
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `201 Created`
- **Cấu trúc Envelope:** `ApiResponse<T>` (Đối tượng đơn lẻ chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 201,
    "data": {
      "withdrawalId": "uuid",
      "amount": 200000,
      "fee": 0,
      "status": "PENDING",
      "requestedAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `INSUFFICIENT_BALANCE` | Insufficient available balance for withdrawal. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `MIN_WITHDRAWAL_LIMIT` | Minimum withdrawal amount is 50,000 VND. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only verified sellers can request withdrawals. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="6-patch-api-v1-admin-wallets-withdrawals-id-"></a>
### 6. `PATCH /api/v1/admin/wallets/withdrawals/{id}`

**Quản trị viên duyệt hoặc từ chối yêu cầu rút tiền của Seller**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/admin/wallets/withdrawals/{id}`
- **Mã tham chiếu Excel**: `WAL-08`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT>"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  {
    "id": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "action": "APPROVE|REJECT",
    "adminNote": "Approved by finance department."
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Cấu trúc Envelope:** `ApiResponse<T>` (Đối tượng đơn lẻ chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "data": {
      "withdrawalId": "uuid",
      "status": "APPROVED",
      "processedAt": "2026-10-26T00:00:00Z",
      "transactionId": "uuid"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ALREADY_PROCESSED` | Withdrawal request has already been processed. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | User does not have administrative permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `WITHDRAWAL_NOT_FOUND` | Withdrawal request was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="7-get-api-v1-wallets-transactions"></a>
### 7. `GET /api/v1/wallets/transactions`

**Tra cứu lịch sử biến động sổ cái (wallet_transactions) cá nhân**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/wallets/transactions`
- **Mã tham chiếu Excel**: `WAL-09`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT>"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Query Parameters:**
  ```json
  {
    "page": 1,
    "limit": 20,
    "type": "ALL|DEPOSIT|WITHDRAWAL|PAYMENT|REFUND",
    "fromDate": "2026-10-01",
    "toDate": "2026-10-26"
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Cấu trúc Envelope:** `ApiResponse<PagedData<T>>` (Danh sách có phân trang chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "data": {
      "items": [
        {
          "id": "uuid",
          "type": "PAYMENT",
          "amount": -480000,
          "balanceAfter": 1020000,
          "status": "SUCCESS",
          "note": "Payment for order #SUB-01",
          "createdAt": "2026-10-26T00:00:00Z"
        }
      ],
      "pagination": {
        "pageIndex": 1,
        "pageSize": 20,
        "totalCount": 1,
        "totalPages": 1,
        "hasPreviousPage": false,
        "hasNextPage": false
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_INPUT` | Page or limit is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="8-post-api-v1-admin-wallets-walletid-adjustments"></a>
### 8. `POST /api/v1/admin/wallets/{walletId}/adjustments`

**Admin điều chỉnh số dư ví thủ công (Cộng/Trừ tiền kèm kiểm toán)**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/admin/wallets/{walletId}/adjustments`
- **Mã tham chiếu Excel**: `WAL-10`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT>"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  {
    "walletId": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "amount": 50000,
    "reason": "Compensation for dispute resolution #SUB-01",
    "adjustmentType": "CREDIT|DEBIT"
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Cấu trúc Envelope:** `ApiResponse<T>` (Đối tượng đơn lẻ chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "data": {
      "adjustmentId": "uuid",
      "walletId": "uuid",
      "amount": 50000,
      "balanceAfter": 1070000,
      "createdAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_AMOUNT` | Adjustment amount must be greater than zero. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | User does not have administrative permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `WALLET_NOT_FOUND` | Target wallet was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---
