# 01 — TÀI LIỆU API XÁC THỰC, ĐĂNG NHẬP & PHÂN QUYỀN (AUTH & TOKEN API)

> **Dịch vụ phụ trách:** `UserService (Port 8001 / Gateway Proxy: Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `10 endpoints`  

Tài liệu chuẩn hóa toàn bộ các API xác thực người dùng, đăng ký, đăng nhập email/mật khẩu, xác thực OTP email, làm mới Token ngầm (Refresh Token Rotation - RTR), quên mật khẩu và quản lý phiên đăng nhập.

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `POST` | [`/api/v1/auth/register`](#1-post-api-v1-auth-register) | `Public` (Không yêu cầu xác thực) | Người dùng đăng kí tài khoản |
| 2 | `POST` | [`/api/v1/auth/otp/verify`](#2-post-api-v1-auth-otp-verify) | `Public` (Không yêu cầu xác thực) | Xác thực tài khoản người dùng |
| 3 | `POST` | [`/api/v1/auth/otp/resend`](#3-post-api-v1-auth-otp-resend) | `Public` (Không yêu cầu xác thực) | Dùng gửi otp cho đăng kí và cả quên mật khẩu |
| 4 | `POST` | [`/api/v1/auth/login`](#4-post-api-v1-auth-login) | `Public` (Không yêu cầu xác thực) | Đăng nhập hệ thống (Email + Password) |
| 5 | `POST` | [`/api/v1/auth/refresh`](#5-post-api-v1-auth-refresh) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Làm mới Token ngầm (Refresh Token Rotation - RTR) |
| 6 | `POST` | [`/api/v1/auth/password/forgot`](#6-post-api-v1-auth-password-forgot) | `Public` (Không yêu cầu xác thực) | Yêu cầu quên mật khẩu & Gửi OTP khôi phục |
| 7 | `PUT` | [`/api/v1/auth/password/reset`](#7-put-api-v1-auth-password-reset) | `Public` (Không yêu cầu xác thực) | Đặt lại mật khẩu mới bằng mã OTP |
| 8 | `PATCH` | [`/api/v1/auth/password`](#8-patch-api-v1-auth-password) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Cập nhật lại mật khẩu(Đổi mật khẩu) |
| 9 | `POST` | [`/api/v1/auth/logout`](#9-post-api-v1-auth-logout) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Đăng xuất & Thu hồi phiên hiện tại |
| 10 | `POST` | [`/api/v1/auth/logout-all`](#10-post-api-v1-auth-logout-all) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Đăng xuất khỏi toàn bộ thiết bị khác |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-post-api-v1-auth-register"></a>
### 1. `POST /api/v1/auth/register`

**Người dùng đăng kí tài khoản**

- **Quyền hạn (RBAC)**: `Public` (Không yêu cầu xác thực)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auth/register`
- **Mã tham chiếu Excel**: `ADM-23`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "email": "string",
  "password": "string",
  "username": "string (optional, max 100)",
  "firstName": "string",
  "lastName": "string",
  "phoneNumber": "string"
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
    "message": "Đăng ký thành công, vui lòng kiểm tra email để lấy mã OTP xác thực",
    "data": {
      "userId": "uuid",
      "email": "string",
      "otpExpiresIn": "int: 300"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_VALIDATION_FAILED` | The provided input data is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `409` | `ERR_EMAIL_ALREADY_EXISTS` | Email is already registered in the system. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `ERR_USERNAME_ALREADY_EXISTS` | Username is already taken. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `ERR_PHONE_ALREADY_EXISTS` | Phone number is already registered in the system. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="2-post-api-v1-auth-otp-verify"></a>
### 2. `POST /api/v1/auth/otp/verify`

**Xác thực tài khoản người dùng**

- **Quyền hạn (RBAC)**: `Public` (Không yêu cầu xác thực)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auth/otp/verify`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "email": "user@example.com",
    "otp": "123456"
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
    "message": "Kích hoạt tài khoản thành công! Bạn có thể đăng nhập ngay bây giờ",
    "data": {
      "isVerified": "boolean: true",
      "email": "string"
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
| `404` | `ERR_USER_NOT_FOUND` | Account does not exist. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `ERR_ACCOUNT_ALREADY_ACTIVE` | Account has already been verified and activated. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `429` | `ERR_MAX_OTP_ATTEMPTS` | Maximum OTP verification attempts exceeded (max 5 attempts). | Bị giới hạn tốc độ (Rate Limit) -> Chờ thời gian đếm ngược trước khi gửi lại. |

---

<a id="3-post-api-v1-auth-otp-resend"></a>
### 3. `POST /api/v1/auth/otp/resend`

**Dùng gửi otp cho đăng kí và cả quên mật khẩu**

- **Quyền hạn (RBAC)**: `Public` (Không yêu cầu xác thực)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auth/otp/resend`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "email": "string",
  "type": "string: \"REGISTER\" | \"FORGOT_PASSWORD\""
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
    "message": "Mã OTP mới đã được gửi tới email của bạn",
    "data": {
      "email": "string",
      "resendCooldown": "int: 60"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `429` | `ERR_OTP_COOLDOWN` | Please wait at least 60 seconds before requesting another OTP. | Bị giới hạn tốc độ (Rate Limit) -> Chờ thời gian đếm ngược trước khi gửi lại. |
| `429` | `ERR_MAX_DAILY_OTP` | Maximum daily OTP request limit exceeded (max 10 requests). | Bị giới hạn tốc độ (Rate Limit) -> Chờ thời gian đếm ngược trước khi gửi lại. |

---

<a id="4-post-api-v1-auth-login"></a>
### 4. `POST /api/v1/auth/login`

**Đăng nhập hệ thống (Email + Password)**

- **Quyền hạn (RBAC)**: `Public` (Không yêu cầu xác thực)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auth/login`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "identifier": "string (email hoặc username)",
  "password": "string",
  "deviceInfo": "string: browser, OS",
  "ipAddress": "string"
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
    "message": "Đăng nhập thành công",
    "data": {
      "accessToken": "string",
      "refreshToken": "string",
      "tokenType": "Bearer",
      "expiresIn": 3600,
      "user": {
        "id": "uuid",
        "email": "string",
        "username": "string | null",
        "firstName": "string",
        "lastName": "string",
        "roles": "string[]",
        "permissions": "string[]"
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_INVALID_CREDENTIALS` | Invalid username, email, or password. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_ACCOUNT_NOT_VERIFIED` | Account has not been verified with OTP. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `403` | `ERR_ACCOUNT_BANNED` | Account has been locked or banned by an administrator. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `423` | `ERR_ACCOUNT_LOCKED_TEMPORARY` | Account is temporarily locked for 30 minutes due to 5 consecutive failed login attempts. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="5-post-api-v1-auth-refresh"></a>
### 5. `POST /api/v1/auth/refresh`

**Làm mới Token ngầm (Refresh Token Rotation - RTR)**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auth/refresh`

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
  "refreshToken": "string",
  "deviceInfo": "string: browser, OS"
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
    "message": "Làm mới phiên làm việc thành công",
    "data": {
      "accessToken": "string",
      "refreshToken": "string",
      "expiresIn": 3600
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_INVALID_TOKEN` | Token is invalid, expired, or malformed. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `401` | `ERR_TOKEN_REUSED` | Token reuse attack detected, session has been revoked. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `401` | `ERR_SESSION_REVOKED` | Login session has been revoked. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="6-post-api-v1-auth-password-forgot"></a>
### 6. `POST /api/v1/auth/password/forgot`

**Yêu cầu quên mật khẩu & Gửi OTP khôi phục**

- **Quyền hạn (RBAC)**: `Public` (Không yêu cầu xác thực)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auth/password/forgot`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "email": "string"
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
    "message": "Nếu email tồn tại, hướng dẫn đặt lại mật khẩu đã được gửi đến bạn",
    "data": {
      "email": "string"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `429` | `ERR_RATE_LIMIT_EXCEEDED` | Rate limit exceeded, please try again later. | Bị giới hạn tốc độ (Rate Limit) -> Chờ thời gian đếm ngược trước khi gửi lại. |

---

<a id="7-put-api-v1-auth-password-reset"></a>
### 7. `PUT /api/v1/auth/password/reset`

**Đặt lại mật khẩu mới bằng mã OTP**

- **Quyền hạn (RBAC)**: `Public` (Không yêu cầu xác thực)
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/auth/password/reset`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "email": "user@example.com",
    "otpCode": "123456",
    "newPassword": "NewPassword123@",
    "confirmPassword": "NewPassword123@"
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
    "message": "Mật khẩu đã được đặt lại thành công. Vui lòng đăng nhập lại!",
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
| `400` | `ERR_OTP_INVALID_OR_EXPIRED` | The OTP code is invalid or has expired. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ERR_PASSWORD_SAME_AS_OLD` | New password cannot be the same as your recent password. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="8-patch-api-v1-auth-password"></a>
### 8. `PATCH /api/v1/auth/password`

**Cập nhật lại mật khẩu(Đổi mật khẩu)**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/auth/password`

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
  "oldPassword": "string",
  "newPassword": "string",
  "confirmPassword": "string"
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
    "message": "Đổi mật khẩu thành công. Vui lòng đăng nhập lại trên các thiết bị khác",
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
| `400` | `ERR_WRONG_OLD_PASSWORD` | Current password provided is incorrect. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ERR_SAME_PASSWORD` | New password cannot be the same as your old password. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="9-post-api-v1-auth-logout"></a>
### 9. `POST /api/v1/auth/logout`

**Đăng xuất & Thu hồi phiên hiện tại**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auth/logout`

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
  "refreshToken": "string"
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
    "message": "Đăng xuất thành công",
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
| `404` | `ERR_SESSION_NOT_FOUND` | The specified session was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="10-post-api-v1-auth-logout-all"></a>
### 10. `POST /api/v1/auth/logout-all`

**Đăng xuất khỏi toàn bộ thiết bị khác**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/auth/logout-all`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT>"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Request Body:** Không yêu cầu (Empty body).

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Cấu trúc Envelope:** `ApiResponse<T>` (Đối tượng đơn lẻ chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Đã đăng xuất khỏi toàn bộ các thiết bị khác thành công",
    "data": {
      "countRevoked": "int"
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
