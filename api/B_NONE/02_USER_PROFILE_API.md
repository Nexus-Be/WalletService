# 02 — TÀI LIỆU API HỒ SƠ NGƯỜI DÙNG & TÀI KHOẢN (USER PROFILE API)

> **Dịch vụ phụ trách:** `UserService (Port 8001 / Gateway Proxy: Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `24 endpoints`  

Tài liệu chuẩn hóa toàn bộ các API quản lý thông tin cá nhân (Profile), sổ địa chỉ nhận hàng, xác thực danh tính KYC Căn cước công dân, tài khoản ngân hàng thụ hưởng, theo dõi Shop và nộp đơn đăng ký trở thành Người bán (Seller).

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `GET` | [`/api/v1/me/sessions`](#1-get-api-v1-me-sessions) | `Buyer` (Yêu cầu JWT Bearer Token) | Xem danh sách thiết bị đang đăng nhập |
| 2 | `DELETE` | [`/api/v1/me/sessions/{id}`](#2-delete-api-v1-me-sessions-id-) | `Buyer` (Yêu cầu JWT Bearer Token) | Thu hồi / Đăng xuất thiết bị từ xa |
| 3 | `GET` | [`/api/v1/me`](#3-get-api-v1-me) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Xem danh sách cá nhân của user |
| 4 | `PATCH` | [`/api/v1/me`](#4-patch-api-v1-me) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Cập nhật thông tin hồ sơ cá nhân |
| 5 | `GET` | [`/api/v1/me/avatar`](#5-get-api-v1-me-avatar) | `Buyer` (Yêu cầu JWT Bearer Token) | Xem ảnh avatar cá nhân |
| 6 | `POST` | [`/api/v1/me/avatar`](#6-post-api-v1-me-avatar) | `Buyer` (Yêu cầu JWT Bearer Token) | Tải lên và cập nhật avatar cá nhân |
| 7 | `GET` | [`/api/v1/me/reputation`](#7-get-api-v1-me-reputation) | `Buyer` (Yêu cầu JWT Bearer Token) | Xem uy tín của bản thân |
| 8 | `GET` | [`/api/v1/me/penalties`](#8-get-api-v1-me-penalties) | `Buyer` (Yêu cầu JWT Bearer Token) | xem thông tin phạt của bản thân |
| 9 | `GET` | [`/api/v1/user/{userId}`](#9-get-api-v1-user-userid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Xem chi tiết hồ sơ cá nhân của 1 user cụ thể |
| 10 | `GET` | [`/api/v1/user/{userId}/avatar`](#10-get-api-v1-user-userid-avatar) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Xem ảnh avatar của từng user |
| 11 | `GET` | [`/api/v1/seller/{sellerId}`](#11-get-api-v1-seller-sellerid-) | `Seller` | User lấy thông tin cửa hàng của Seller |
| 12 | `GET` | [`/api/v1/user/{userId}/reputation`](#12-get-api-v1-user-userid-reputation) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Xem Thẻ Tóm tắt Hồ sơ Uy tín công khai |
| 13 | `GET` | [`/api/v1/user/{userId}/reputation/history`](#13-get-api-v1-user-userid-reputation-history) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Xem lịch sử uy tín (tăng or giảm) |
| 14 | `POST` | [`/api/v1/report/files/upload`](#14-post-api-v1-report-files-upload) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Upload bằng chứng tố cáo tài khoản vi phạm (User Violation Evidence) |
| 15 | `POST` | [`/api/v1/users/reports`](#15-post-api-v1-users-reports) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Người dùng gửi đơn tố cáo tài khoản vi phạm quy chế |
| 16 | `GET` | [`/api/v1/me/reports`](#16-get-api-v1-me-reports) | `Buyer` (Yêu cầu JWT Bearer Token) | Người dùng xem danh sách các báo cáo do mình đã gửi |
| 17 | `POST` | [`/api/v1/seller/registration`](#17-post-api-v1-seller-registration) | `Seller` | Seller tạo đơn đăng kí |
| 18 | `POST` | [`/api/v1/seller/registration/otp/resend`](#18-post-api-v1-seller-registration-otp-resend) | `Seller` | Gửi lại otp cho tài khoản Seller |
| 19 | `POST` | [`/api/v1/seller/registration/documents`](#19-post-api-v1-seller-registration-documents) | `Seller` | Upload tài liệu Seller(CCCD, ....) |
| 20 | `POST` | [`/api/v1/seller/otp/verify`](#20-post-api-v1-seller-otp-verify) | `Seller` | Xác thực mã otp của Seller |
| 21 | `GET` | [`/api/v1/seller/registration`](#21-get-api-v1-seller-registration) | `Seller` | Seller xem đơn (đã được duyệt làm Seller hay chưa) |
| 22 | `GET` | [`/api/v1/seller/profile`](#22-get-api-v1-seller-profile) | `Seller` | Seller tự coi thông tin shop |
| 23 | `PATCH` | [`/api/v1/seller/profile`](#23-patch-api-v1-seller-profile) | `Seller` | Cập nhật thông tin cá nhân hồ sơ cho Seller |
| 24 | `GET` | [`/api/v1/me/bids`](#24-get-api-v1-me-bids) | `Buyer` (Yêu cầu JWT Bearer Token) | Buyer coi được các phiên đấu giá mà bản thân đã từng tham gia |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-get-api-v1-me-sessions"></a>
### 1. `GET /api/v1/me/sessions`

**Xem danh sách thiết bị đang đăng nhập**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/me/sessions`

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
- **Cấu trúc Envelope:** `ApiResponse<PagedData<T>>` (Danh sách có phân trang chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Lấy danh sách phiên đăng nhập thành công",
    "data": {
      "items": [
        {
          "sessionId": "uuid",
          "deviceInfo": "string",
          "ipAddress": "string",
          "lastActiveAt": "timestamp",
          "isCurrent": "boolean"
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
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="2-delete-api-v1-me-sessions-id-"></a>
### 2. `DELETE /api/v1/me/sessions/{id}`

**Thu hồi / Đăng xuất thiết bị từ xa**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `DELETE`
- **Đường dẫn**: `/api/v1/me/sessions/{id}`

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

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Cấu trúc Envelope:** `ApiResponse<T>` (Đối tượng đơn lẻ chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Đã thu hồi phiên đăng nhập thành công",
    "data": {
      "revokedSessionId": "uuid"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `403` | `ERR_FORBIDDEN` | You do not have permission to manage another user session. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_SESSION_NOT_FOUND` | The specified session was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="3-get-api-v1-me"></a>
### 3. `GET /api/v1/me`

**Xem danh sách cá nhân của user**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/me`

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
    "message": "Lấy thông tin cá nhân thành công",
    "data": {
      "id": "uuid",
      "email": "string",
      "username": "string | null",
      "firstName": "string",
      "lastName": "string",
      "phoneNumber": "string",
      "avatarUrl": "string",
      "address": "string",
      "reputationScore": "int",
      "trustLevel": "string",
      "roles": "string[]",
      "permissions": "string[]",
      "isSeller": "boolean",
      "createdAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, token is missing or expired. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `ERR_USER_NOT_FOUND` | User was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="4-patch-api-v1-me"></a>
### 4. `PATCH /api/v1/me`

**Cập nhật thông tin hồ sơ cá nhân**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/me`

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
    "username": "string (optional, max 100)",
    "firstName": "string",
    "lastName": "string",
    "phoneNumber": "string",
    "address": {
      "provinceId": 1,
      "districtId": 1,
      "wardCode": "001",
      "streetAddress": "string"
    }
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
    "message": "Cập nhật hồ sơ thành công",
    "data": {
      "id": "uuid",
      "username": "string | null",
      "firstName": "string",
      "lastName": "string",
      "phoneNumber": "string",
      "avatarUrl": "string",
      "address": "string",
      "updatedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_VALIDATION_FAILED` | The provided input data or address is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `409` | `ERR_USERNAME_DUPLICATE` | Username is already taken by another account. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `ERR_PHONE_DUPLICATE` | Phone number is already registered by another account. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="5-get-api-v1-me-avatar"></a>
### 5. `GET /api/v1/me/avatar`

**Xem ảnh avatar cá nhân**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/me/avatar`

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
    "message": "Lấy avatar thành công",
    "data": {
      "userId": "uuid",
      "avatarUrl": "string"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, token is missing or expired. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `ERR_USER_NOT_FOUND` | User was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="6-post-api-v1-me-avatar"></a>
### 6. `POST /api/v1/me/avatar`

**Tải lên và cập nhật avatar cá nhân**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/me/avatar`

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
  "file": "binary (image/jpeg, image/png, image/webp, max 5MB)"
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
    "message": "Cập nhật ảnh đại diện thành công",
    "data": {
      "avatarUrl": "string",
      "updatedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_INVALID_FILE_TYPE` | Unsupported file format, only JPG, PNG, and WEBP are accepted. | Hiển thị thông báo tương ứng cho người dùng. |
| `413` | `ERR_FILE_TOO_LARGE` | File size exceeds maximum allowed limit of 5mb. | Hiển thị thông báo tương ứng cho người dùng. |
| `500` | `ERR_STORAGE_UPLOAD_FAILED` | Internal server error occurred while storing uploaded file. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="7-get-api-v1-me-reputation"></a>
### 7. `GET /api/v1/me/reputation`

**Xem uy tín của bản thân**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/me/reputation`

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
    "message": "Lấy thông tin uy tín cá nhân thành công",
    "data": {
      "userId": "uuid",
      "reputationScore": "int",
      "trustLevel": "string",
      "canBid": "boolean",
      "canSell": "boolean",
      "positiveRatingCount": "int",
      "negativeRatingCount": "int",
      "nextLevelThreshold": "int"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, token is invalid or missing. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `ERR_USER_NOT_FOUND` | User account was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="8-get-api-v1-me-penalties"></a>
### 8. `GET /api/v1/me/penalties`

**xem thông tin phạt của bản thân**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/me/penalties`

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
  "page": "int (default: 1)",
  "limit": "int (default: 20)"
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
    "message": "Lấy lịch sử vi phạm thành công",
    "data": {
      "items": [
        {
          "id": "uuid",
          "violationType": "string",
          "reason": "string",
          "penaltyApplied": "string",
          "pointsDeducted": "int",
          "status": "string",
          "issuedAt": "timestamp",
          "expiresAt": "timestamp"
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
| `401` | `ERR_UNAUTHORIZED` | Authentication required, session is missing or expired. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="9-get-api-v1-user-userid-"></a>
### 9. `GET /api/v1/user/{userId}`

**Xem chi tiết hồ sơ cá nhân của 1 user cụ thể**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/user/{userId}`

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
  "userId": "uuid"
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
    "message": "Lấy thông tin hồ sơ công khai thành công",
    "data": {
      "id": "uuid",
      "username": "string | null",
      "firstName": "string",
      "lastName": "string",
      "avatarUrl": "string",
      "reputationScore": "int",
      "trustLevel": "string",
      "badges": "string[]",
      "isSeller": "boolean",
      "joinedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_USER_INACTIVE` | Account is not active or has been deleted. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `ERR_USER_NOT_FOUND` | User was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="10-get-api-v1-user-userid-avatar"></a>
### 10. `GET /api/v1/user/{userId}/avatar`

**Xem ảnh avatar của từng user**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/user/{userId}/avatar`

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
  "userId": "uuid"
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
    "message": "Lấy avatar thành công",
    "data": {
      "userId": "uuid",
      "avatarUrl": "string"
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

<a id="11-get-api-v1-seller-sellerid-"></a>
### 11. `GET /api/v1/seller/{sellerId}`

**User lấy thông tin cửa hàng của Seller**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/seller/{sellerId}`

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
  "sellerId": "uuid"
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
    "message": "Lấy thông tin cửa hàng thành công",
    "data": {
      "sellerId": "uuid",
      "shopName": "string",
      "avatarUrl": "string",
      "ratingAvg": "decimal",
      "totalReviews": "int",
      "joinedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `404` | `ERR_SELLER_NOT_FOUND` | Shop does not exist or has been deactivated. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="12-get-api-v1-user-userid-reputation"></a>
### 12. `GET /api/v1/user/{userId}/reputation`

**Xem Thẻ Tóm tắt Hồ sơ Uy tín công khai**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/user/{userId}/reputation`

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
  "userId": "uuid"
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
    "message": "Lấy tóm tắt uy tín thành công",
    "data": {
      "userId": "uuid",
      "reputationScore": "int",
      "trustLevel": "string",
      "badges": "string[]",
      "canBid": "boolean",
      "canSell": "boolean",
      "totalReviews": "int",
      "positiveRatio": "decimal"
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

<a id="13-get-api-v1-user-userid-reputation-history"></a>
### 13. `GET /api/v1/user/{userId}/reputation/history`

**Xem lịch sử uy tín (tăng or giảm)**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/user/{userId}/reputation/history`

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
  { "userId": "uuid" }
  ```

- **Query Parameters:**
  ```json
  {
  "page": "int (default: 1)",
  "limit": "int (default: 20)"
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
    "message": "Lấy lịch sử uy tín thành công",
    "data": {
      "items": [
        {
          "id": "int",
          "changeAmount": "int",
          "scoreAfter": "int",
          "reason": "string",
          "referenceType": "string",
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
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_FORBIDDEN` | You do not have permission to view other users reputation history. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="14-post-api-v1-report-files-upload"></a>
### 14. `POST /api/v1/report/files/upload`

**Upload bằng chứng tố cáo tài khoản vi phạm (User Violation Evidence)**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/report/files/upload`

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
  "files": "binary[] (<= 5MB)"
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
    "message": "Upload bằng chứng tố cáo thành công",
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
| `400` | `ERR_INVALID_FILE_TYPE` | Invalid file format, only images up to 5mb are accepted. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="15-post-api-v1-users-reports"></a>
### 15. `POST /api/v1/users/reports`

**Người dùng gửi đơn tố cáo tài khoản vi phạm quy chế**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/users/reports`

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
    "reportedUserId": "uuid",
    "violationType": "SPAM|FRAUD|INAPPROPRIATE_CONTENT",
    "description": "Nguoi dung co hanh vi gian lan.",
    "evidenceUrls": ["https://img.example.com/evidence1.png"]
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
    "message": "Gửi đơn tố cáo thành công. Nhân viên sẽ thụ lý trong 24h",
    "data": {
      "reportId": "uuid",
      "status": "PENDING",
      "submittedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_CANNOT_REPORT_SELF` | You cannot submit a report against yourself. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ERR_VALIDATION_FAILED` | Description content must be at least 10 characters long. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `ERR_USER_NOT_FOUND` | Reported user does not exist. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="16-get-api-v1-me-reports"></a>
### 16. `GET /api/v1/me/reports`

**Người dùng xem danh sách các báo cáo do mình đã gửi**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/me/reports`

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
  "page": "int (default: 1)",
  "limit": "int (default: 20)",
  "status": "string (optional: PENDING, RESOLVED, REJECTED)"
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
    "message": "Lấy danh sách tố cáo đã gửi thành công",
    "data": {
      "items": [
        {
          "reportId": "uuid",
          "targetType": "string",
          "targetId": "uuid",
          "reason": "string",
          "evidenceUrls": "string[]",
          "status": "string",
          "adminNote": "string",
          "createdAt": "timestamp",
          "resolvedAt": "timestamp"
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
| `401` | `ERR_UNAUTHORIZED` | Authentication required, token is missing or expired. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="17-post-api-v1-seller-registration"></a>
### 17. `POST /api/v1/seller/registration`

**Seller tạo đơn đăng kí**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/seller/registration`

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
    "shopName": "string",
    "businessEmail": "string",
    "hotline": "string",
    "warehouseAddress": {
      "provinceId": 1,
      "districtId": 1,
      "wardCode": "001",
      "streetAddress": "string"
    },
    "identityCardNumber": "string"
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
    "message": "Hồ sơ mở Shop đã được tiếp nhận và chuyển sang trạng thái chờ duyệt",
    "data": {
      "registrationId": "uuid",
      "status": "PENDING_APPROVAL",
      "submittedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_GHN_WAREHOUSE_INVALID` | Ghn warehouse address is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ERR_REPUTATION_TOO_LOW` | Reputation score is insufficient to open a shop (minimum 50 required). | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `409` | `ERR_SHOP_NAME_TAKEN` | Shop name already exists. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `ERR_REQUEST_ALREADY_PENDING` | A registration request is already pending review. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `ERR_ALREADY_SELLER` | User is already registered as a seller. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="18-post-api-v1-seller-registration-otp-resend"></a>
### 18. `POST /api/v1/seller/registration/otp/resend`

**Gửi lại otp cho tài khoản Seller**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/seller/registration/otp/resend`

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
  "registrationId": "uuid",
  "businessEmail": "string (optional)"
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
    "message": "Mã OTP xác thực mở Shop đã được gửi lại tới email",
    "data": {
      "registrationId": "uuid",
      "cooldown": 60
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `ERR_REGISTRATION_NOT_FOUND` | Registration request was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `429` | `ERR_OTP_COOLDOWN` | Please wait at least 60 seconds before requesting another OTP. | Bị giới hạn tốc độ (Rate Limit) -> Chờ thời gian đếm ngược trước khi gửi lại. |

---

<a id="19-post-api-v1-seller-registration-documents"></a>
### 19. `POST /api/v1/seller/registration/documents`

**Upload tài liệu Seller(CCCD, ....)**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/seller/registration/documents`

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
  "registrationId": "uuid",
  "frontCccd": "binary",
  "backCccd": "binary",
  "businessLicense": "binary (optional)"
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
    "message": "Tải lên tài liệu định danh thành công",
    "data": {
      "documentUrls": "string[]"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_INVALID_FILE_TYPE` | Invalid document format, only JPG, PNG, and PDF are accepted. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `ERR_REGISTRATION_NOT_FOUND` | Registration request was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="20-post-api-v1-seller-otp-verify"></a>
### 20. `POST /api/v1/seller/otp/verify`

**Xác thực mã otp của Seller**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/seller/otp/verify`

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
  "registrationId": "uuid",
  "otpCode": "string (6 digits)"
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
    "message": "Xác thực OTP thành công. Hồ sơ đã chuyển sang chờ Staff duyệt",
    "data": {
      "registrationId": "uuid",
      "status": "PENDING_APPROVAL"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_OTP_INVALID` | The OTP code provided is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ERR_OTP_EXPIRED` | The OTP code has expired. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `ERR_REGISTRATION_NOT_FOUND` | Registration request was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="21-get-api-v1-seller-registration"></a>
### 21. `GET /api/v1/seller/registration`

**Seller xem đơn (đã được duyệt làm Seller hay chưa)**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/seller/registration`

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
    "message": "Lấy trạng thái hồ sơ thành công",
    "data": {
      "registrationId": "uuid",
      "status": "PENDING_APPROVAL | APPROVED | REJECTED",
      "rejectionReason": "string | null",
      "submittedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, token is missing or expired. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="22-get-api-v1-seller-profile"></a>
### 22. `GET /api/v1/seller/profile`

**Seller tự coi thông tin shop**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/seller/profile`

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
    "message": "Lấy thông tin Shop thành công",
    "data": {
      "shopId": "uuid",
      "shopName": "string",
      "hotline": "string",
      "pickupAddress": "string",
      "ghnShopId": "string",
      "ratingAvg": "decimal",
      "totalProducts": "int"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_FORBIDDEN_NOT_SELLER` | Account does not have the seller role. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="23-patch-api-v1-seller-profile"></a>
### 23. `PATCH /api/v1/seller/profile`

**Cập nhật thông tin cá nhân hồ sơ cho Seller**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/seller/profile`

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
    "shopName": "string",
    "hotline": "string",
    "pickupAddress": {
      "provinceId": 1,
      "districtId": 1,
      "wardCode": "001",
      "streetAddress": "string"
    },
    "description": "string"
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
    "message": "Cập nhật thông tin Shop thành công",
    "data": {
      "shopId": "uuid",
      "shopName": "string",
      "updatedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_FORBIDDEN` | You do not have permission to modify this shop. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `409` | `ERR_SHOP_NAME_TAKEN` | Shop name is already taken. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="24-get-api-v1-me-bids"></a>
### 24. `GET /api/v1/me/bids`

**Buyer coi được các phiên đấu giá mà bản thân đã từng tham gia**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/me/bids`
- **Mã tham chiếu Excel**: `AUC-08`

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
          "productName": "string",
          "primaryImageUrl": "string | null",
          "auctionStatus": "AuctionStatus",
          "myHighestBidAmount": "number",
          "currentPriceAmount": "number | null",
          "isLeading": "boolean",
          "endsAt": "datetime",
          "lastBidAt": "datetime"
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

---
