# 06 — TÀI LIỆU API ĐẶT HÀNG, THANH TOÁN & ĐÁNH GIÁ (ORDERS & CHECKOUT API)

> **Dịch vụ phụ trách:** `CommerceService (Port 8003 / Gateway Proxy: Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `13 endpoints`  

Tài liệu chuẩn hóa toàn bộ các API quy trình thanh toán: Xem trước chi phí đơn hàng (Checkout Preview), Tạo đơn hàng, Quản lý đơn con theo Shop (Sub-orders), Tiếp nhận & hủy đơn, Đánh giá sản phẩm và người bán, Hoàn tiền và Webhook thanh toán.

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `GET` | [`/api/v1/user/{userId}/reviews`](#1-get-api-v1-user-userid-reviews) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Xem danh sách đánh giá công khai của 1 user/Shop |
| 2 | `POST` | [`/api/v1/review/files/upload`](#2-post-api-v1-review-files-upload) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Upload đánh giá sản phẩm |
| 3 | `POST` | [`/api/v1/reviews`](#3-post-api-v1-reviews) | `Seller` | Đánh giá đối tác 2 chiều (Buyer ↔ Seller) |
| 4 | `POST` | [`/api/v1/orders`](#4-post-api-v1-orders) | `Buyer` (Yêu cầu JWT Bearer Token) | tạo order |
| 5 | `GET` | [`/api/v1/order/{orderId}`](#5-get-api-v1-order-orderid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | lấy thôn tin chi tiết của order đó |
| 6 | `GET` | [`/api/v1/subOrder/{subOrderId}`](#6-get-api-v1-suborder-suborderid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | coi thong tin chi tiết của 1 sub order |
| 7 | `POST` | [`/api/v1/orders/{orderId}/cancel`](#7-post-api-v1-orders-orderid-cancel) | `Buyer` (Yêu cầu JWT Bearer Token) | cancel cái order đó, chưa chuyển khoản, chưa thanh toán |
| 8 | `POST` | [`/api/v1/payments/webhook`](#8-post-api-v1-payments-webhook) | `Authenticated User` (Yêu cầu JWT Bearer Token) | thành công, chuyển |
| 9 | `POST` | [`/api/v1/orders/{orderId}/accept`](#9-post-api-v1-orders-orderid-accept) | `Seller` | seller chấp nhận cái đơn đó hay ko/ xác nhận |
| 10 | `POST` | [`/api/v1/orders/{orderId}/reject`](#10-post-api-v1-orders-orderid-reject) | `Seller` | seller chấp nhận cái đơn đó hay ko/ từ chối |
| 11 | `POST` | [`/api/v1/order/{orderId}/user/{userId}`](#11-post-api-v1-order-orderid-user-userid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | đánh giá người dùng |
| 12 | `PATCH` | [`/api/v1/order/{orderId}/user/{userId}`](#12-patch-api-v1-order-orderid-user-userid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | chỉnh sửa đánh giá người dùng |
| 13 | `POST` | [`/api/v1/subOrder/{subOrderId}/refund`](#13-post-api-v1-suborder-suborderid-refund) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Hoàn tiền sub-order riêng lẻ kèm kiểm tra điều kiện trạng thái |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-get-api-v1-user-userid-reviews"></a>
### 1. `GET /api/v1/user/{userId}/reviews`

**Xem danh sách đánh giá công khai của 1 user/Shop**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/user/{userId}/reviews`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  { "userId": "uuid" }
  ```

- **Query Parameters:**
  ```json
  {
  "page": "int (default: 1)",
  "limit": "int (default: 10)",
  "filterRating": "int (optional: 1..5)"
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
    "message": "Lấy danh sách đánh giá thành công",
    "data": {
      "items": [
        {
          "id": "uuid",
          "reviewerName": "string",
          "rating": "int",
          "comment": "string",
          "mediaUrls": "string[]",
          "createdAt": "timestamp"
        }
      ],
      "pagination": {
        "pageIndex": 1,
        "pageSize": 20,
        "totalCount": 100,
        "totalPages": 5,
        "hasPreviousPage": false,
        "hasNextPage": true
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `404` | `ERR_USER_NOT_FOUND` | User was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="2-post-api-v1-review-files-upload"></a>
### 2. `POST /api/v1/review/files/upload`

**Upload đánh giá sản phẩm**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/review/files/upload`

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
  "files": "binary[] (max 5 files)"
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
    "message": "Upload ảnh đánh giá thành công",
    "data": {
      "fileUrls": "string[]"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_FILE_LIMIT_EXCEEDED` | Maximum limit of 5 attached images exceeded. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="3-post-api-v1-reviews"></a>
### 3. `POST /api/v1/reviews`

**Đánh giá đối tác 2 chiều (Buyer ↔ Seller)**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/reviews`

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
    "transactionType": "ORDER|AUCTION",
    "transactionId": "uuid",
    "targetUserId": "uuid",
    "rating": 5,
    "comment": "Giao hang nhanh, dong goi can than.",
    "mediaUrls": ["https://img.example.com/review1.png"]
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
    "message": "Gửi đánh giá thành công! Điểm uy tín của đối tác đã được cập nhật",
    "data": {
      "reviewId": "uuid",
      "newTargetReputationScore": "int"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_TRANSACTION_NOT_COMPLETED` | Transaction is not yet completed. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ERR_REVIEW_WINDOW_EXPIRED` | Review submission period of 7 days has expired. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_NOT_TRANSACTION_PARTY` | You are not a participant in this transaction. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `409` | `ERR_REVIEW_ALREADY_EXISTS` | A review has already been submitted for this transaction. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="4-post-api-v1-orders"></a>
### 4. `POST /api/v1/orders`

**tạo order**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/orders`
- **Mã tham chiếu Excel**: `ORD-01`

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
    "quoteId": "uuid",
    "paymentMethod": "WALLET|VNPAY|COD",
    "shippingAddress": "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
    "contactPhone": "0901234567",
    "note": "Giao gio hanh chinh"
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
      "orderId": "uuid",
      "orderNo": "ORD-20261026-0001",
      "totalAmount": 480000,
      "status": "PENDING_PAYMENT",
      "paymentDeadlineAt": "2026-10-26T02:00:00Z",
      "paymentUrl": null,
      "subOrderIds": [
        "uuid"
      ],
      "createdAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_INPUT` | Shipping address and phone number are required. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `QUOTE_EXPIRED` | Checkout quote has expired. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `409` | `IDEMPOTENCY_CONFLICT` | Request with this Idempotency-Key is already processed. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="5-get-api-v1-order-orderid-"></a>
### 5. `GET /api/v1/order/{orderId}`

**lấy thôn tin chi tiết của order đó**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/order/{orderId}`
- **Mã tham chiếu Excel**: `ORD-02`

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
    "orderId": "uuid"
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
      "orderId": "uuid",
      "orderNo": "ORD-20261026-0001",
      "buyerId": "uuid",
      "status": "PENDING_PAYMENT",
      "totalAmount": 480000,
      "paymentMethod": "WALLET",
      "shippingAddress": "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
      "contactPhone": "0901234567",
      "subOrders": [
        {
          "subOrderId": "uuid",
          "subOrderNo": "SUB-20261026-0001",
          "sellerId": "uuid",
          "shopName": "Shop Cong Nghe",
          "status": "PENDING_PAYMENT",
          "itemsAmount": 500000,
          "shippingFee": 30000,
          "discountAmount": 50000,
          "totalAmount": 480000,
          "items": [
            {
              "orderItemId": "uuid",
              "productId": "uuid",
              "skuId": "uuid",
              "productName": "Chuot gaming khong day",
              "skuCode": "MOUSE-WL-01",
              "imageUrl": "https://img.example.com/mouse.png",
              "quantity": 1,
              "unitPrice": 500000,
              "lineTotal": 500000
            }
          ]
        }
      ],
      "createdAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You do not have permission to view this order. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ORDER_NOT_FOUND` | Order does not exist or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="6-get-api-v1-suborder-suborderid-"></a>
### 6. `GET /api/v1/subOrder/{subOrderId}`

**coi thong tin chi tiết của 1 sub order**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/subOrder/{subOrderId}`
- **Mã tham chiếu Excel**: `ORD-03`

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
    "subOrderId": "uuid"
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
          "orderItemId": "uuid",
          "productId": "uuid",
          "skuId": "uuid",
          "productName": "Chuot gaming khong day",
          "skuCode": "MOUSE-WL-01",
          "quantity": 1,
          "unitPrice": 500000,
          "lineTotal": 500000
        }
      ],
      "pagination": {
        "pageIndex": 1,
        "pageSize": 20,
        "totalCount": 100,
        "totalPages": 5,
        "hasPreviousPage": false,
        "hasNextPage": true
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You do not have permission to view this sub-order. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `SUB_ORDER_NOT_FOUND` | Sub-order was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="7-post-api-v1-orders-orderid-cancel"></a>
### 7. `POST /api/v1/orders/{orderId}/cancel`

**cancel cái order đó, chưa chuyển khoản, chưa thanh toán**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/orders/{orderId}/cancel`
- **Mã tham chiếu Excel**: `ORD-04`

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
    "orderId": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "reasonCode": "BUYER_CHANGED_MIND|FOUND_BETTER_PRICE|OTHER",
    "reasonText": "Bought by mistake."
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
    "message": "Order has been successfully cancelled.",
    "data": {
      "orderId": "uuid",
      "status": "CANCELLED",
      "cancelledAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ORDER_CANNOT_BE_CANCELLED` | Order cannot be cancelled in its current state. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You do not have permission to cancel this order. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ORDER_NOT_FOUND` | Order does not exist or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="8-post-api-v1-payments-webhook"></a>
### 8. `POST /api/v1/payments/webhook`

**thành công, chuyển**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/payments/webhook`
- **Mã tham chiếu Excel**: `PAY-01`

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
    "transactionId": "uuid",
    "amount": 480000,
    "status": "SUCCESS|FAILED",
    "paymentMethod": "VNPAY",
    "paidAt": "2026-10-26T00:00:00Z"
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
    "message": "Payment webhook processed successfully.",
    "data": {
      "success": true
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `INVALID_SIGNATURE` | Invalid webhook signature. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `ORDER_NOT_FOUND` | Order not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="9-post-api-v1-orders-orderid-accept"></a>
### 9. `POST /api/v1/orders/{orderId}/accept`

**seller chấp nhận cái đơn đó hay ko/ xác nhận**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/orders/{orderId}/accept`
- **Mã tham chiếu Excel**: `ORD-05`

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
    "orderId": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "subOrderId": "uuid",
    "note": "Order confirmed, preparing items."
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
      "subOrderId": "uuid",
      "status": "SELLER_ACCEPTED",
      "acceptedAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `INVALID_ORDER_STATUS` | Order cannot be accepted in its current status. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only the seller of this sub-order can accept it. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `SUB_ORDER_NOT_FOUND` | Sub-order was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="10-post-api-v1-orders-orderid-reject"></a>
### 10. `POST /api/v1/orders/{orderId}/reject`

**seller chấp nhận cái đơn đó hay ko/ từ chối**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/orders/{orderId}/reject`
- **Mã tham chiếu Excel**: `ORD-06`

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
    "orderId": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "subOrderId": "uuid",
    "reasonCode": "OUT_OF_STOCK|CANNOT_FULFILL|OTHER",
    "reasonText": "Items are temporarily out of stock."
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
      "subOrderId": "uuid",
      "status": "REJECTED",
      "rejectedAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `INVALID_ORDER_STATUS` | Order cannot be rejected in its current status. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only the seller of this sub-order can reject it. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `SUB_ORDER_NOT_FOUND` | Sub-order was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="11-post-api-v1-order-orderid-user-userid-"></a>
### 11. `POST /api/v1/order/{orderId}/user/{userId}`

**đánh giá người dùng**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/order/{orderId}/user/{userId}`
- **Mã tham chiếu Excel**: `ORD-08`

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
    "orderId": "uuid",
    "userId": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "rating": 5,
    "feedback": "Shop phan hoi nhanh, giao hang dung hen."
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
      "feedbackId": "uuid",
      "orderId": "uuid",
      "targetUserId": "uuid",
      "rating": 5,
      "createdAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_RATING` | Rating must be an integer between 1 and 5. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `USER_NOT_FOUND` | Target user was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="12-patch-api-v1-order-orderid-user-userid-"></a>
### 12. `PATCH /api/v1/order/{orderId}/user/{userId}`

**chỉnh sửa đánh giá người dùng**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/order/{orderId}/user/{userId}`
- **Mã tham chiếu Excel**: `ORD-08`

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
    "orderId": "uuid",
    "userId": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "rating": 4,
    "feedback": "Shop da ho tro doi hang nhanh chong."
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
      "feedbackId": "uuid",
      "orderId": "uuid",
      "targetUserId": "uuid",
      "rating": 4,
      "updatedAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_RATING` | Rating must be an integer between 1 and 5. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You can only edit your own feedback. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `FEEDBACK_NOT_FOUND` | Feedback does not exist. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="13-post-api-v1-suborder-suborderid-refund"></a>
### 13. `POST /api/v1/subOrder/{subOrderId}/refund`

**Hoàn tiền sub-order riêng lẻ kèm kiểm tra điều kiện trạng thái**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/subOrder/{subOrderId}/refund`
- **Mã tham chiếu Excel**: `ORD-11`

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
    "subOrderId": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "reason": "DEFECTIVE_PRODUCT|MISSING_ITEMS|OTHER",
    "note": "San pham bi loi nguon, yeu cau hoan tien."
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
      "subOrderId": "uuid",
      "refundAmount": 480000,
      "status": "REFUNDED",
      "refundedAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `INVALID_ORDER_STATUS` | Refund can only be requested for eligible sub-orders. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `SUB_ORDER_NOT_FOUND` | Sub-order was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---
