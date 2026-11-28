# 10 — TÀI LIỆU API THÔNG BÁO & TIN NHẮN (NOTIFICATIONS API)

> **Dịch vụ phụ trách:** `NotificationService (Port 8007 / Gateway Proxy: Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `14 endpoints`  

Tài liệu chuẩn hóa toàn bộ các API hệ thống thông báo đa kênh: Danh sách thông báo trong ứng dụng (In-app), Đánh dấu đã đọc, Đăng ký và hủy mã FCM Device Token cho thiết bị di động, Cấu hình kênh nhận tin, Gửi email thử nghiệm và Giám sát sức khỏe Broker.

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `POST` | [`/api/v1/notifications/devices`](#1-post-api-v1-notifications-devices) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Đăng ký hoặc làm mới FCM/APNs device token để nhận push notification |
| 2 | `DELETE` | [`/api/v1/notifications/devices`](#2-delete-api-v1-notifications-devices) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Thu hồi hoặc vô hiệu hóa device token khi logout hoặc tắt quyền push |
| 3 | `GET` | [`/api/v1/notifications/preferences`](#3-get-api-v1-notifications-preferences) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Lấy danh sách cấu hình bật/tắt nhận thông báo của người dùng |
| 4 | `PUT` | [`/api/v1/notifications/preferences`](#4-put-api-v1-notifications-preferences) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Cập nhật tùy chọn bật/tắt nhận thông báo theo loại sự kiện và kênh gửi |
| 5 | `GET` | [`/api/v1/notifications/me`](#5-get-api-v1-notifications-me) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Lấy danh sách lịch sử thông báo in-app (quả chuông) kèm số lượng chưa đọc |
| 6 | `PATCH` | [`/api/v1/notifications/me/read`](#6-patch-api-v1-notifications-me-read) | `Buyer` (Yêu cầu JWT Bearer Token) | Đánh dấu một số hoặc toàn bộ thông báo trong hộp thư là đã đọc |
| 7 | `GET` | [`/api/v1/admin/notifications/channels/{code}`](#7-get-api-v1-admin-notifications-channels-code-) | `Admin` / `SuperAdmin` | Admin xem trạng thái và cấu hình kênh gửi thông báo |
| 8 | `PUT` | [`/api/v1/admin/notifications/channels/{code}`](#8-put-api-v1-admin-notifications-channels-code-) | `Admin` / `SuperAdmin` | Admin cập nhật cấu hình và bật/tắt kênh gửi thông báo |
| 9 | `GET` | [`/api/v1/admin/notifications/templates`](#9-get-api-v1-admin-notifications-templates) | `Admin` / `SuperAdmin` | Admin xem danh sách mẫu thông báo (Template Engine) |
| 10 | `POST` | [`/api/v1/admin/notifications/templates`](#10-post-api-v1-admin-notifications-templates) | `Admin` / `SuperAdmin` | Admin tạo mẫu nội dung thông báo mới |
| 11 | `GET` | [`/api/v1/admin/notifications`](#11-get-api-v1-admin-notifications) | `Admin` / `SuperAdmin` | Admin/Staff tra cứu lịch sử gửi thông báo và nhật ký lỗi delivery |
| 12 | `GET` | [`/api/v1/admin/event-logs`](#12-get-api-v1-admin-event-logs) | `Admin` / `SuperAdmin` | Admin tra cứu nhật ký sự kiện kỹ thuật message bus (90 ngày) |
| 13 | `GET` | [`/api/v1/admin/event-logs/{id}`](#13-get-api-v1-admin-event-logs-id-) | `Admin` / `SuperAdmin` | Admin xem chi tiết toàn bộ raw JSON payload của sự kiện kỹ thuật |
| 14 | `GET` | [`/api/v1/admin/health/broker`](#14-get-api-v1-admin-health-broker) | `Admin` / `SuperAdmin` | Admin theo dõi trạng thái sức khỏe, latency và queue depth RabbitMQ |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-post-api-v1-notifications-devices"></a>
### 1. `POST /api/v1/notifications/devices`

**Đăng ký hoặc làm mới FCM/APNs device token để nhận push notification**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/notifications/devices`
- **Mã tham chiếu Excel**: `NOTI-01`

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
    "platform": "WEB|IOS|ANDROID",
    "token": "string"
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
      "deviceId": "uuid",
      "userId": "uuid",
      "platform": "WEB|IOS|ANDROID",
      "isActive": true,
      "lastSeenAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_DEVICE_TOKEN` | Platform or token is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="2-delete-api-v1-notifications-devices"></a>
### 2. `DELETE /api/v1/notifications/devices`

**Thu hồi hoặc vô hiệu hóa device token khi logout hoặc tắt quyền push**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `DELETE`
- **Đường dẫn**: `/api/v1/notifications/devices`
- **Mã tham chiếu Excel**: `NOTI-02`

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
    "token": "string"
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Cấu trúc Envelope:** `ApiResponse<T>` (Đối tượng đơn lẻ chuẩn).
- **Payload JSON Mẫu:**
  ```json
  204 No Content
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_TOKEN` | Token is missing or invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `DEVICE_TOKEN_NOT_FOUND` | Device token was not found for this user. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="3-get-api-v1-notifications-preferences"></a>
### 3. `GET /api/v1/notifications/preferences`

**Lấy danh sách cấu hình bật/tắt nhận thông báo của người dùng**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/notifications/preferences`
- **Mã tham chiếu Excel**: `NOTI-03`

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
    "data": {
      "items": [
        {
          "notificationType": "ORDER_PAID|AUCTION_OUTBID|PROMOTION",
          "channelCode": "EMAIL|PUSH|IN_APP",
          "isEnabled": true,
          "isMandatory": true
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

---

<a id="4-put-api-v1-notifications-preferences"></a>
### 4. `PUT /api/v1/notifications/preferences`

**Cập nhật tùy chọn bật/tắt nhận thông báo theo loại sự kiện và kênh gửi**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/notifications/preferences`
- **Mã tham chiếu Excel**: `NOTI-04`

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
    "preferences": [
      {
        "notificationType": "ORDER_PAID",
        "channelCode": "EMAIL",
        "isEnabled": true
      }
    ]
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
      "updatedCount": 1,
      "preferences": [
        {
          "notificationType": "ORDER_PAID",
          "channelCode": "EMAIL",
          "isEnabled": true
        }
      ]
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_PREFERENCES` | Notification type or channel code is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `422` | `MANDATORY_NOTIFICATION_CANNOT_DISABLE` | Mandatory security or transaction notification cannot be disabled. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="5-get-api-v1-notifications-me"></a>
### 5. `GET /api/v1/notifications/me`

**Lấy danh sách lịch sử thông báo in-app (quả chuông) kèm số lượng chưa đọc**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/notifications/me`
- **Mã tham chiếu Excel**: `NOTI-05`

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
    "isRead": false,
    "notificationType": "ORDER_PAID",
    "pageIndex": 1,
    "pageSize": 20
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
          "notificationType": "ORDER_PAID",
          "title": "Don hang #SUB-01 da duoc thanh toan",
          "body": "Don hang tri gia 480.000d da duoc thanh toan thanh cong.",
          "payload": {
            "orderId": "uuid",
            "subOrderId": "uuid"
          },
          "isRead": false,
          "readAt": null,
          "createdAt": "2026-10-26T00:00:00Z"
        }
      ],
      "pagination": {
        "pageIndex": 1,
        "pageSize": 20,
        "totalCount": 10,
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
| `400` | `VAL_INVALID_PAGE` | PageIndex or pageSize is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="6-patch-api-v1-notifications-me-read"></a>
### 6. `PATCH /api/v1/notifications/me/read`

**Đánh dấu một số hoặc toàn bộ thông báo trong hộp thư là đã đọc**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/notifications/me/read`
- **Mã tham chiếu Excel**: `NOTI-06`

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
    "notificationIds": ["uuid"],
    "markAll": false
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
      "updatedCount": 1,
      "unreadCount": 2
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_INPUT` | Notification IDs list is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="7-get-api-v1-admin-notifications-channels-code-"></a>
### 7. `GET /api/v1/admin/notifications/channels/{code}`

**Admin xem trạng thái và cấu hình kênh gửi thông báo**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/notifications/channels/{code}`
- **Mã tham chiếu Excel**: `NOTI-07`

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
    "code": "EMAIL|PUSH|IN_APP|SMS"
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
      "id": "uuid",
      "code": "EMAIL",
      "name": "Email Service",
      "provider": "SMTP",
      "config": {
        "host": "smtp.example.com",
        "port": 587,
        "fromAddress": "noreply@shop.example.com"
      },
      "isEnabled": true,
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
| `403` | `FORBIDDEN` | User does not have administrative permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `CHANNEL_NOT_FOUND` | Notification channel code was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="8-put-api-v1-admin-notifications-channels-code-"></a>
### 8. `PUT /api/v1/admin/notifications/channels/{code}`

**Admin cập nhật cấu hình và bật/tắt kênh gửi thông báo**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/notifications/channels/{code}`
- **Mã tham chiếu Excel**: `NOTI-07`

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
    "code": "EMAIL|PUSH|IN_APP|SMS"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "name": "Email Service",
    "provider": "SMTP",
    "config": {
      "host": "smtp.example.com",
      "port": 587,
      "fromAddress": "noreply@shop.example.com"
    },
    "isEnabled": true
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
      "id": "uuid",
      "code": "EMAIL",
      "name": "Email Service",
      "provider": "SMTP",
      "config": {
        "host": "smtp.example.com",
        "port": 587,
        "fromAddress": "noreply@shop.example.com"
      },
      "isEnabled": true,
      "updatedAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `SECRET_IN_CONFIG_FORBIDDEN` | Secrets like passwords or API keys are not allowed in config body. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | User does not have administrative permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `CHANNEL_NOT_FOUND` | Notification channel code was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="9-get-api-v1-admin-notifications-templates"></a>
### 9. `GET /api/v1/admin/notifications/templates`

**Admin xem danh sách mẫu thông báo (Template Engine)**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/notifications/templates`
- **Mã tham chiếu Excel**: `NOTI-08`

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
    "channelCode": "EMAIL",
    "notificationType": "ORDER_PAID"
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
          "code": "ORDER_PAID_EMAIL",
          "notificationType": "ORDER_PAID",
          "channelCode": "EMAIL",
          "subjectTemplate": "Don hang {{orderNo}} thanh toan thanh cong",
          "bodyTemplate": "<p>Xin chao {{userName}}, don hang {{orderNo}} da duoc xac nhan.</p>",
          "isMandatory": true,
          "locale": "vi-VN",
          "isActive": true
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
| `403` | `FORBIDDEN` | User does not have administrative permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="10-post-api-v1-admin-notifications-templates"></a>
### 10. `POST /api/v1/admin/notifications/templates`

**Admin tạo mẫu nội dung thông báo mới**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/admin/notifications/templates`
- **Mã tham chiếu Excel**: `NOTI-08`

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
    "code": "ORDER_PAID_EMAIL",
    "notificationType": "ORDER_PAID",
    "channelCode": "EMAIL",
    "subjectTemplate": "Don hang {{orderNo}} thanh toan thanh cong",
    "bodyTemplate": "<p>Xin chao {{userName}}, don hang {{orderNo}} da duoc xac nhan.</p>",
    "isMandatory": true,
    "locale": "vi-VN",
    "isActive": true
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
      "id": "uuid",
      "code": "ORDER_PAID_EMAIL",
      "notificationType": "ORDER_PAID",
      "channelCode": "EMAIL",
      "isMandatory": true,
      "locale": "vi-VN",
      "isActive": true
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `INVALID_TEMPLATE_SYNTAX` | Template syntax is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | User does not have administrative permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `409` | `TEMPLATE_CODE_DUPLICATED` | Template code and locale already exist. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="11-get-api-v1-admin-notifications"></a>
### 11. `GET /api/v1/admin/notifications`

**Admin/Staff tra cứu lịch sử gửi thông báo và nhật ký lỗi delivery**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/notifications`
- **Mã tham chiếu Excel**: `NOTI-09`

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
    "userId": "uuid",
    "notificationType": "ORDER_PAID",
    "status": "COMPLETED|PENDING|FAILED",
    "pageIndex": 1,
    "pageSize": 20
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
          "notificationId": "uuid",
          "userId": "uuid",
          "notificationType": "ORDER_PAID",
          "status": "COMPLETED",
          "createdAt": "2026-10-26T00:00:00Z",
          "deliveries": [
            {
              "deliveryId": "uuid",
              "channelCode": "EMAIL",
              "destination": "u***@gmail.com",
              "status": "SENT",
              "attemptCount": 1
            }
          ]
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
| `400` | `VAL_INVALID_FILTER` | Query filter parameters are invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | User does not have administrative permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="12-get-api-v1-admin-event-logs"></a>
### 12. `GET /api/v1/admin/event-logs`

**Admin tra cứu nhật ký sự kiện kỹ thuật message bus (90 ngày)**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/event-logs`
- **Mã tham chiếu Excel**: `NOTI-13`

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
    "sourceService": "Commerce|Auction|User|Wallet",
    "eventType": "OrderPaid",
    "pageIndex": 1,
    "pageSize": 20
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
          "eventId": "uuid",
          "eventType": "OrderPaid",
          "sourceService": "Commerce",
          "actorId": "uuid",
          "occurredAt": "2026-10-26T00:00:00Z"
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
| `400` | `VAL_INVALID_FILTER` | Query date range or filter is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | User does not have administrative permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="13-get-api-v1-admin-event-logs-id-"></a>
### 13. `GET /api/v1/admin/event-logs/{id}`

**Admin xem chi tiết toàn bộ raw JSON payload của sự kiện kỹ thuật**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/event-logs/{id}`
- **Mã tham chiếu Excel**: `NOTI-14`

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
    "data": {
      "id": "uuid",
      "eventId": "uuid",
      "eventType": "OrderPaid",
      "sourceService": "Commerce",
      "actorId": "uuid",
      "payload": {
        "orderId": "uuid",
        "totalAmount": 480000
      },
      "occurredAt": "2026-10-26T00:00:00Z",
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
| `403` | `FORBIDDEN` | User does not have administrative permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `EVENT_LOG_NOT_FOUND` | Event log entry was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="14-get-api-v1-admin-health-broker"></a>
### 14. `GET /api/v1/admin/health/broker`

**Admin theo dõi trạng thái sức khỏe, latency và queue depth RabbitMQ**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/health/broker`
- **Mã tham chiếu Excel**: `NOTI-16`

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
    "limit": 50
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
      "currentStatus": "UP",
      "lastCheckedAt": "2026-10-26T00:00:00Z",
      "latestLatencyMs": 5,
      "latestQueueDepth": 0,
      "history": [
        {
          "id": "uuid",
          "brokerName": "rabbitmq",
          "status": "UP",
          "latencyMs": 5,
          "queueDepth": 0,
          "checkedAt": "2026-10-26T00:00:00Z"
        }
      ]
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | User does not have administrative permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---
