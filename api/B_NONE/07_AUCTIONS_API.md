# 07 — TÀI LIỆU API ĐẤU GIÁ TRỰC TUYẾN & ĐẶT GIÁ (AUCTIONS & BIDDING API)

> **Dịch vụ phụ trách:** `AuctionService (Port 8005 / Gateway Proxy: Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `17 endpoints`  

Tài liệu chuẩn hóa toàn bộ các API hệ thống đấu giá trực tuyến: Danh sách phiên đấu giá công khai, Đặt cọc tham gia, Đặt giá đấu (Bid), Mua ngay (Buy Now), Lịch sử đặt giá, Quản lý phiên đấu giá cho Người bán và Phê duyệt phiên của Quản trị viên.

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `GET` | [`/api/v1/product/auctions`](#1-get-api-v1-product-auctions) | `Seller` | coi đươc toàn bộ các sản phẩm seller đó đã đấu giá |
| 2 | `GET` | [`/api/v1/product/auction/{auctionId}`](#2-get-api-v1-product-auction-auctionid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | coi thông tin chi tiết của sản phẩm đấu giá đó |
| 3 | `GET` | [`/api/v1/auctions`](#3-get-api-v1-auctions) | `Public` / Mọi người dùng | User xem danh sách các phiên đấu giá |
| 4 | `GET` | [`/api/v1/auctions/{auctionId}`](#4-get-api-v1-auctions-auctionid-) | `Public` / Mọi người dùng | Xem chi tiết 1 phiên đấu giá |
| 5 | `POST` | [`/api/v1/auctions`](#5-post-api-v1-auctions) | `Seller` | Seller tạo phiên đấu giá, chọn 1 sản phẩm có rồi |
| 6 | `PUT` | [`/api/v1/auctions/{auctionId}`](#6-put-api-v1-auctions-auctionid-) | `Seller` | Seller chỉnh sửa phiên đấu giá(khi chưa bắt đầu) |
| 7 | `POST` | [`/api/v1/auctions/{auctionId}/cancel`](#7-post-api-v1-auctions-auctionid-cancel) | `Seller` | Seller hủy phiên đấu giá(khi phiên đấu giá chưa bắt đầu) |
| 8 | `POST` | [`/api/v1/auctions/{auctionId}/recreate`](#8-post-api-v1-auctions-auctionid-recreate) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Tạo lại phiên mới cho sản phẩm ở phiên cũ (đã bị đấu giá thất bại) |
| 9 | `GET` | [`/api/v1/auctions/{auctionId}/status-history`](#9-get-api-v1-auctions-auctionid-status-history) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Nhật kí đổi trạng thái của từng auction |
| 10 | `GET` | [`/api/v1/seller/auctions`](#10-get-api-v1-seller-auctions) | `Seller` | Seller xem toàn bộ phiên đấu giá của mình |
| 11 | `POST` | [`/api/v1/admin/auctions/{auctionId}/force-cancel`](#11-post-api-v1-admin-auctions-auctionid-force-cancel) | `Admin` / `SuperAdmin` | Admin cưỡng chế hủy phiên đấu giá khi có bất thường |
| 12 | `GET` | [`/api/v1/admin/auctions/{auctionId}/saga`](#12-get-api-v1-admin-auctions-auctionid-saga) | `Admin` / `SuperAdmin` | Admin theo dõi xem phiên đang ở bước nào của luồng đơn hàng(sau khi chốt người thắng -> commerce) |
| 13 | `POST` | [`/api/v1/auctions/{auctionId}/bids`](#13-post-api-v1-auctions-auctionid-bids) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Buyer đặt giá cho một phiên đấu giá |
| 14 | `GET` | [`/api/v1/auctions/{auctionId}/bids`](#14-get-api-v1-auctions-auctionid-bids) | `Authenticated User` (Yêu cầu JWT Bearer Token) | User xem được lịch sử đặt giá của phiên đang tham gia |
| 15 | `GET` | [`/api/v1/auctions/{auctionId}/result`](#15-get-api-v1-auctions-auctionid-result) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Xem kết quả của phiên đấu giá |
| 16 | `GET` | [`/internal/v1/auctions/status?productId={productId}`](#16-get-internal-v1-auctions-status-productid-productid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Catalog lấy trạng thái phiên đấu giá gần nhất của một sản phẩm, để ẩn sản phẩm khỏi kho ảo khi đã bán xong |
| 17 | `GET` | [`/internal/v1/auctions/{auctionId}/summary`](#17-get-internal-v1-auctions-auctionid-summary) | `Public` / Mọi người dùng | Catalog lấy thông tin chi tiết một phiên: người đang trả giá cao nhất, giá khởi điểm, giá hiện tại, bước giá |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-get-api-v1-product-auctions"></a>
### 1. `GET /api/v1/product/auctions`

**coi đươc toàn bộ các sản phẩm seller đó đã đấu giá**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/product/auctions`
- **Mã tham chiếu Excel**: `CAT-06`

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
  "status": "DRAFT|ACTIVE|INACTIVE",
  "page": "int",
  "pageSize": "int (tối đa 100)"
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
          "productId": "uuid",
          "name": "string",
          "brand": "string",
          "saleType": "REGULAR|AUCTION",
          "minPrice": "decimal | null",
          "maxPrice": "decimal | null",
          "primaryImageUrl": "string | null",
          "avgRating": "decimal | null",
          "status": "DRAFT|ACTIVE|INACTIVE",
          "auctionStatus": "DRAFT|SCHEDULED|RUNNING|ENDED|SETTLED|FAILED|CANCELLED | null",
          "isOpen": "boolean | null",
          "isSold": "boolean | null"
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
| `400` | `VALIDATION_ERROR` | Page and pageSize parameters must be positive integers. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only sellers or administrators can perform this action. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="2-get-api-v1-product-auction-auctionid-"></a>
### 2. `GET /api/v1/product/auction/{auctionId}`

**coi thông tin chi tiết của sản phẩm đấu giá đó**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/product/auction/{auctionId}`
- **Mã tham chiếu Excel**: `AUC-35 + CAT-05`

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
  {"auctionId": "uuid"}
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
      "auction": {
        "auctionId": "uuid",
        "status": "DRAFT|SCHEDULED|RUNNING|ENDED|SETTLED|FAILED|CANCELLED",
        "startingPriceAmount": "number",
        "bidStepAmount": "number",
        "currentPriceAmount": "number | null",
        "currentLeadingBidderMasked": "string | null",
        "bidCount": "int",
        "startsAt": "datetime",
        "endsAt": "datetime",
        "extensionCount": "int"
      },
      "product": "(như dòng 111, CAT-05: Product, Category, Variants, Images, Rating)"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Auctionid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the seller of this auction and not an administrator. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `404` | `PRODUCT_NOT_FOUND` | Product associated with this auction no longer exists. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `503` | `AUCTION_SERVICE_UNAVAILABLE` | Auction service is currently unavailable. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="3-get-api-v1-auctions"></a>
### 3. `GET /api/v1/auctions`

**User xem danh sách các phiên đấu giá**

- **Quyền hạn (RBAC)**: `Public` / Mọi người dùng
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/auctions`
- **Mã tham chiếu Excel**: `AUC-01`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Query Parameters:**
  ```json
  {
  "page": "int (optional, default 1)",
  "pageSize": "int (optional, default 20, tối đa 100)",
  "status": "AuctionStatus (optional)"
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
          "auctionId": "UUID",
          "productId": "UUID",
          "skuId": "UUID",
          "sellerId": "UUID",
          "productName": "string",
          "primaryImageUrl": "string | null",
          "status": "AuctionStatus",
          "startPriceAmount": "number",
          "currentPriceAmount": "number | null",
          "bidCount": "int",
          "scheduledStartAt": "datetime",
          "scheduledEndAt": "datetime"
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
| `400` | `VALIDATION_ERROR` | Invalid pagination parameters or status is not a valid auctionstatus enum. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="4-get-api-v1-auctions-auctionid-"></a>
### 4. `GET /api/v1/auctions/{auctionId}`

**Xem chi tiết 1 phiên đấu giá**

- **Quyền hạn (RBAC)**: `Public` / Mọi người dùng
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/auctions/{auctionId}`
- **Mã tham chiếu Excel**: `AUC-02`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  { "auctionId": "UUID" }
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
      "auctionId": "UUID",
      "productId": "UUID",
      "skuId": "UUID",
      "sellerId": "UUID",
      "productName": "string",
      "description": "string | null",
      "images": [
        {
          "url": "string",
          "isPrimary": "boolean"
        }
      ],
      "specifications": "object | null",
      "status": "AuctionStatus",
      "startPriceAmount": "number",
      "bidStepAmount": "number",
      "currentPriceAmount": "number | null",
      "bidCount": "int",
      "leadingBidderDisplayName": "string | null",
      "scheduledStartAt": "datetime",
      "originalEndAt": "datetime",
      "scheduledEndAt": "datetime",
      "startedAt": "datetime | null",
      "endedAt": "datetime | null",
      "extensionCount": "int",
      "maxExtensionCount": "int",
      "antiSnipingExtensionMinutes": "int",
      "minReputationToBid": "int",
      "recreatedFromAuctionId": "UUID | null",
      "createdAt": "datetime"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Auctionid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="5-post-api-v1-auctions"></a>
### 5. `POST /api/v1/auctions`

**Seller tạo phiên đấu giá, chọn 1 sản phẩm có rồi**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auctions`
- **Mã tham chiếu Excel**: `AUC-10`

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
  "productId": "UUID",
  "skuId": "UUID",
  "startPriceAmount": "number",
  "bidStepAmount": "number (optional)",
  "scheduledStartAt": "datetime",
  "scheduledEndAt": "datetime"
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
      "auctionId": "UUID",
      "productId": "UUID",
      "skuId": "UUID",
      "productName": "string",
      "status": "SCHEDULED",
      "startPriceAmount": "number",
      "bidStepAmount": "number",
      "scheduledStartAt": "datetime",
      "scheduledEndAt": "datetime",
      "minReputationToBid": "int",
      "antiSnipingExtensionMinutes": "int",
      "maxExtensionCount": "int",
      "createdAt": "datetime"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Start price < 0, bid step <= 0, start time in past, or end time not after start time. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `SELLER_NOT_ELIGIBLE` | Seller is not approved or reputation score is below auction threshold (minimum 50). | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `409` | `PRODUCT_ALREADY_IN_AUCTION` | Product already has an ongoing or scheduled auction. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `INVENTORY_UNAVAILABLE` | Inventory stock hold failed. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `422` | `PRODUCT_NOT_ELIGIBLE` | Product is invalid, inactive, does not belong to seller, or SKU mismatch. | Hiển thị thông báo tương ứng cho người dùng. |
| `422` | `INVALID_SCHEDULE` | Start time cannot exceed 30 days or duration cannot be under 1 hour. | Hiển thị thông báo tương ứng cho người dùng. |
| `503` | `USER_SERVICE_UNAVAILABLE` | User service is currently unavailable. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="6-put-api-v1-auctions-auctionid-"></a>
### 6. `PUT /api/v1/auctions/{auctionId}`

**Seller chỉnh sửa phiên đấu giá(khi chưa bắt đầu)**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/auctions/{auctionId}`
- **Mã tham chiếu Excel**: `AUC-11`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  { "auctionId": "UUID" }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "startPriceAmount": "number (optional)",
  "bidStepAmount": "number (optional)",
  "scheduledStartAt": "datetime (optional)",
  "scheduledEndAt": "datetime (optional)"
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
      "auctionId": "UUID",
      "status": "SCHEDULED",
      "startPriceAmount": "number",
      "bidStepAmount": "number",
      "scheduledStartAt": "datetime",
      "scheduledEndAt": "datetime",
      "updatedAt": "datetime"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Invalid auctionid, empty payload, or invalid pricing/scheduling values. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the seller of this auction. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `AUCTION_NOT_EDITABLE` | Auction has already started and cannot be edited. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `422` | `INVALID_SCHEDULE` | Start time cannot exceed 30 days or duration cannot be under 1 hour. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="7-post-api-v1-auctions-auctionid-cancel"></a>
### 7. `POST /api/v1/auctions/{auctionId}/cancel`

**Seller hủy phiên đấu giá(khi phiên đấu giá chưa bắt đầu)**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auctions/{auctionId}/cancel`
- **Mã tham chiếu Excel**: `AUC-12`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  { "auctionId": "UUID" }
  ```

- **Request Body (JSON Payload):**
  ```json
  { "reason": "string (optional)" }
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
      "auctionId": "UUID",
      "fromStatus": "AuctionStatus",
      "status": "CANCELLED",
      "reason": "string | null",
      "cancelledAt": "datetime"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Auctionid is not a valid UUID format or cancellation reason is empty. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the seller of this auction. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `AUCTION_HAS_BIDS` | Auction already has bids, only an administrator can cancel it. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `CONCURRENT_UPDATE` | Auction status was modified concurrently by another operation. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `422` | `INVALID_STATE_TRANSITION` | Auction status is invalid for cancellation. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="8-post-api-v1-auctions-auctionid-recreate"></a>
### 8. `POST /api/v1/auctions/{auctionId}/recreate`

**Tạo lại phiên mới cho sản phẩm ở phiên cũ (đã bị đấu giá thất bại)**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auctions/{auctionId}/recreate`
- **Mã tham chiếu Excel**: `AUC-13`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  { "auctionId": "UUID (phiên cũ)" }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "startPriceAmount": "number",
  "bidStepAmount": "number (optional; bỏ trống thì dùng bước giá mặc định theo config)",
  "scheduledStartAt": "datetime",
  "scheduledEndAt": "datetime"
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
      "auctionId": "UUID (phiên mới)",
      "recreatedFromAuctionId": "UUID",
      "productId": "UUID",
      "skuId": "UUID",
      "status": "SCHEDULED",
      "startPriceAmount": "number",
      "bidStepAmount": "number",
      "scheduledStartAt": "datetime",
      "scheduledEndAt": "datetime",
      "createdAt": "datetime"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Invalid auctionid, start price < 0, bid step <= 0, start time in past, or invalid schedule. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Previous auction does not belong to the calling seller. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `403` | `SELLER_NOT_ELIGIBLE` | Reputation score is below the auction hosting threshold (minimum 50). | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `AUCTION_NOT_FOUND` | Previous auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `PRODUCT_ALREADY_IN_AUCTION` | Product already has an ongoing or scheduled auction. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `INVENTORY_UNAVAILABLE` | Inventory stock hold failed. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `422` | `INVALID_RECREATE_SOURCE` | Previous auction is not in failed status. | Hiển thị thông báo tương ứng cho người dùng. |
| `422` | `PRODUCT_NOT_ELIGIBLE` | Product is no longer eligible. | Hiển thị thông báo tương ứng cho người dùng. |
| `422` | `INVALID_SCHEDULE` | Start time cannot exceed 30 days or duration cannot be under 1 hour. | Hiển thị thông báo tương ứng cho người dùng. |
| `503` | `USER_SERVICE_UNAVAILABLE` | User service is currently unavailable. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="9-get-api-v1-auctions-auctionid-status-history"></a>
### 9. `GET /api/v1/auctions/{auctionId}/status-history`

**Nhật kí đổi trạng thái của từng auction**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/auctions/{auctionId}/status-history`
- **Mã tham chiếu Excel**: `AUC-18`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  { "auctionId": "UUID" }
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
          "id": "UUID",
          "fromStatus": "AuctionStatus | null",
          "toStatus": "AuctionStatus",
          "reason": "string | null",
          "changedBy": "UUID | null",
          "changedAt": "datetime"
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
| `400` | `VALIDATION_ERROR` | Auctionid is not a valid UUID format or invalid pagination parameters. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Administrator or staff privileges are required. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="10-get-api-v1-seller-auctions"></a>
### 10. `GET /api/v1/seller/auctions`

**Seller xem toàn bộ phiên đấu giá của mình**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/seller/auctions`
- **Mã tham chiếu Excel**: `AUC-14`

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
  "page": "int (optional, default 1)",
  "pageSize": "int (optional, default 20, tối đa 100)",
  "status": "AuctionStatus (optional)"
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
          "auctionId": "UUID",
          "productId": "UUID",
          "skuId": "UUID",
          "productName": "string",
          "primaryImageUrl": "string | null",
          "status": "AuctionStatus",
          "startPriceAmount": "number",
          "currentPriceAmount": "number | null",
          "bidCount": "int",
          "scheduledStartAt": "datetime",
          "scheduledEndAt": "datetime",
          "createdAt": "datetime"
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
| `400` | `VALIDATION_ERROR` | Invalid pagination parameters or status is not a valid auctionstatus enum. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only registered sellers can perform this action. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="11-post-api-v1-admin-auctions-auctionid-force-cancel"></a>
### 11. `POST /api/v1/admin/auctions/{auctionId}/force-cancel`

**Admin cưỡng chế hủy phiên đấu giá khi có bất thường**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/admin/auctions/{auctionId}/force-cancel`
- **Mã tham chiếu Excel**: `AUC-15`

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
  { "auctionId": "UUID" }
  ```

- **Request Body (JSON Payload):**
  ```json
  { "reason": "string (bắt buộc)" }
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
      "auctionId": "UUID",
      "fromStatus": "AuctionStatus",
      "status": "CANCELLED",
      "reason": "string",
      "cancelledBy": "UUID",
      "cancelledAt": "datetime"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Auctionid is not a valid UUID format or cancellation reason is empty. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Administrator privileges required, support staff cannot perform this action. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `CONCURRENT_UPDATE` | Auction status was modified concurrently by another operation. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `422` | `INVALID_STATE_TRANSITION` | Auction status is invalid for cancellation (ended auctions cannot be cancelled). | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="12-get-api-v1-admin-auctions-auctionid-saga"></a>
### 12. `GET /api/v1/admin/auctions/{auctionId}/saga`

**Admin theo dõi xem phiên đang ở bước nào của luồng đơn hàng(sau khi chốt người thắng -> commerce)**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/auctions/{auctionId}/saga`
- **Mã tham chiếu Excel**: `AUC-19`

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
  { "auctionId": "UUID" }
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
      "auctionId": "UUID",
      "sagaType": "string",
      "currentState": "string",
      "stateData": "object",
      "createdAt": "datetime",
      "updatedAt": "datetime"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Auctionid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Administrator or staff privileges are required. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found or has no winning saga. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="13-post-api-v1-auctions-auctionid-bids"></a>
### 13. `POST /api/v1/auctions/{auctionId}/bids`

**Buyer đặt giá cho một phiên đấu giá**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auctions/{auctionId}/bids`
- **Mã tham chiếu Excel**: `AUC-07`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  { "auctionId": "UUID" }
  ```

- **Request Body (JSON Payload):**
  ```json
  { "amount": "number" }
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
      "bidId": "UUID",
      "auctionId": "UUID",
      "bidSeq": "int",
      "amount": "number",
      "placedAt": "datetime",
      "currentPriceAmount": "number",
      "bidCount": "int",
      "scheduledEndAt": "datetime",
      "extensionCount": "int"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Invalid auctionid, missing Idempotency-Key or If-Match header, or invalid bid amount. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `BIDDER_NOT_ELIGIBLE` | Reputation score is below bidding threshold (minimum 40) or account is locked. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `CONCURRENT_UPDATE` | Concurrent update detected (rowversion mismatch or earlier bid). | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `410` | `AUCTION_ENDED` | Auction has ended or scheduled end time has passed. | Hiển thị thông báo tương ứng cho người dùng. |
| `422` | `AUCTION_NOT_RUNNING` | Auction is not in running status. | Hiển thị thông báo tương ứng cho người dùng. |
| `422` | `BID_TOO_LOW` | Bid amount is below the minimum required bid increment. | Hiển thị thông báo tương ứng cho người dùng. |
| `422` | `IDEMPOTENCY_KEY_REUSED` | Idempotency key reused with different request payload. | Hiển thị thông báo tương ứng cho người dùng. |
| `503` | `USER_SERVICE_UNAVAILABLE` | User service is currently unavailable. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="14-get-api-v1-auctions-auctionid-bids"></a>
### 14. `GET /api/v1/auctions/{auctionId}/bids`

**User xem được lịch sử đặt giá của phiên đang tham gia**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/auctions/{auctionId}/bids`
- **Mã tham chiếu Excel**: `AUC-03`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  { "auctionId": "UUID" }
  ```

- **Query Parameters:**
  ```json
  {
  "page": "int (optional, default 1)",
  "pageSize": "int (optional, default 20, tối đa 100)"
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
          "bidId": "UUID",
          "sequenceNo": "int",
          "bidderId": "UUID",
          "bidderDisplayName": "string",
          "amount": "number",
          "placedAt": "datetime",
          "isOutbid": "boolean",
          "isWinning": "boolean",
          "isInvalidated": "boolean"
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
| `400` | `VALIDATION_ERROR` | Auctionid is not a valid UUID format or invalid pagination parameters. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="15-get-api-v1-auctions-auctionid-result"></a>
### 15. `GET /api/v1/auctions/{auctionId}/result`

**Xem kết quả của phiên đấu giá**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/auctions/{auctionId}/result`
- **Mã tham chiếu Excel**: `AUC-09`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  { "auctionId": "UUID" }
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
      "auctionId": "UUID",
      "status": "AuctionStatus",
      "hasWinner": "boolean",
      "winnerId": "UUID | null",
      "winnerDisplayName": "string | null",
      "sellerId": "UUID",
      "winningBidId": "UUID | null",
      "finalPriceAmount": "number | null",
      "determinedAt": "datetime",
      "orderId": "UUID | null",
      "paymentDeadlineAt": "datetime | null",
      "paidAt": "datetime | null"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Auctionid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the winner, seller, or administrator of this auction. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="16-get-internal-v1-auctions-status-productid-productid-"></a>
### 16. `GET /internal/v1/auctions/status?productId={productId}`

**Catalog lấy trạng thái phiên đấu giá gần nhất của một sản phẩm, để ẩn sản phẩm khỏi kho ảo khi đã bán xong**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/internal/v1/auctions/status?productId={productId}`
- **Mã tham chiếu Excel**: `AUC-34`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  {
    "productId": "uuid"
  }
  ```

- **Query Parameters:**
  ```json
  { "productId": "UUID" }
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
      "productId": "UUID",
      "auctionId": "UUID | null",
      "status": "",
      "isOpen": "boolean",
      "isSold": "boolean"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Productid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `403` | `FORBIDDEN` | Only internal catalog service can call this endpoint. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="17-get-internal-v1-auctions-auctionid-summary"></a>
### 17. `GET /internal/v1/auctions/{auctionId}/summary`

**Catalog lấy thông tin chi tiết một phiên: người đang trả giá cao nhất, giá khởi điểm, giá hiện tại, bước giá**

- **Quyền hạn (RBAC)**: `Public` / Mọi người dùng
- **Phương thức**: `GET`
- **Đường dẫn**: `/internal/v1/auctions/{auctionId}/summary`
- **Mã tham chiếu Excel**: `AUC-35`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  {
    "auctionId": "uuid"
  }
  ```

- **Query Parameters:**
  ```json
  {
  "auctionId": "UUID"
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
      "auctionId": "UUID",
      "productId": "UUID",
      "sellerId": "UUID",
      "status": "AuctionStatus",
      "startingPriceAmount": "number",
      "bidStepAmount": "number",
      "currentPriceAmount": "number | null",
      "currentLeadingBidderId": "UUID | null",
      "currentLeadingBidderMasked": "string | null",
      "bidCount": "int",
      "startsAt": "datetime",
      "endsAt": "datetime",
      "extensionCount": "int"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Auctionid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `403` | `FORBIDDEN` | Only internal catalog service can call this endpoint. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `AUCTION_NOT_FOUND` | Auction was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---
