# 03 — TÀI LIỆU API QUẢN TRỊ VIÊN & PHÂN QUYỀN HỆ THỐNG (ADMIN & USER MANAGEMENT API)

> **Dịch vụ phụ trách:** `UserService (Port 8001 / Gateway Proxy: Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `26 endpoints`  

Tài liệu chuẩn hóa toàn bộ các API dành cho Quản trị viên (Admin/SuperAdmin) quản lý người dùng, khóa/mở khóa tài khoản (Ban/Unban), phê duyệt hồ sơ người bán, xử lý tố cáo vi phạm, ẩn đánh giá tiêu cực và cấu hình phân quyền hệ thống.

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `GET` | [`/api/v1/admin/seller-registrations`](#1-get-api-v1-admin-seller-registrations) | `Admin` / `SuperAdmin` | Danh sách hồ sơ Seller chờ duyệt |
| 2 | `GET` | [`/api/v1/admin/seller-registrations/{registrationId}`](#2-get-api-v1-admin-seller-registrations-registrationid-) | `Admin` / `SuperAdmin` | Xem chi tiết hồ sơ mở Shop và tài liệu chứng từ định danh |
| 3 | `PUT` | [`/api/v1/admin/seller-registrations/{registrationId}/approve`](#3-put-api-v1-admin-seller-registrations-registrationid-approve) | `Admin` / `SuperAdmin` | Staff duyệt hồ sơ Seller |
| 4 | `PUT` | [`/api/v1/admin/seller-registrations/{registrationId}/reject`](#4-put-api-v1-admin-seller-registrations-registrationid-reject) | `Admin` / `SuperAdmin` | Staff từ chối hồ sơ Seller |
| 5 | `GET` | [`/api/v1/admin/reports`](#5-get-api-v1-admin-reports) | `Admin` / `SuperAdmin` | Admin/Staff xem danh sách các đơn tố cáo tài khoản |
| 6 | `PUT` | [`/api/v1/admin/reports/{reportId}/resolve`](#6-put-api-v1-admin-reports-reportid-resolve) | `Admin` / `SuperAdmin` | Staff xử lý chấp thuận tố cáo & áp dụng chế tài kỷ luật |
| 7 | `PUT` | [`/api/v1/admin/reports/{reportId}/dismiss`](#7-put-api-v1-admin-reports-reportid-dismiss) | `Admin` / `SuperAdmin` | Staff bác bỏ đơn tố cáo sai sự thật / không đủ chứng cứ |
| 8 | `PUT` | [`/api/v1/admin/reviews/{id}/hide`](#8-put-api-v1-admin-reviews-id-hide) | `Admin` / `SuperAdmin` | Staff/Admin ẩn đánh giá vi phạm tiêu chuẩn |
| 9 | `GET` | [`/api/v1/admin/user`](#9-get-api-v1-admin-user) | `Admin` / `SuperAdmin` | Admin xem danh sách các user |
| 10 | `POST` | [`/api/v1/admin/user`](#10-post-api-v1-admin-user) | `Admin` / `SuperAdmin` | Admin tạo user mới |
| 11 | `GET` | [`/api/v1/admin/user/{userId}`](#11-get-api-v1-admin-user-userid-) | `Admin` / `SuperAdmin` | Xem chi tiết hồ sơ cá nhân, phân quyền & uy tín |
| 12 | `PUT` | [`/api/v1/admin/user/{userId}`](#12-put-api-v1-admin-user-userid-) | `Admin` / `SuperAdmin` | Admin cập nhật thông tin của user cụ thể |
| 13 | `DELETE` | [`/api/v1/admin/user/{userId}`](#13-delete-api-v1-admin-user-userid-) | `Admin` / `SuperAdmin` | Xóa MỀM 1 user khỏi hệ thống |
| 14 | `PATCH` | [`/api/v1/admin/user/{userId}/ban`](#14-patch-api-v1-admin-user-userid-ban) | `Admin` / `SuperAdmin` | Admin ban 1 user |
| 15 | `PATCH` | [`/api/v1/admin/user/{userId}/unban`](#15-patch-api-v1-admin-user-userid-unban) | `Admin` / `SuperAdmin` | Admin hủy ban 1 user |
| 16 | `GET` | [`/api/v1/admin/sellers/{sellerId}`](#16-get-api-v1-admin-sellers-sellerid-) | `Admin` / `SuperAdmin` | Admin lấy thông tin cửa hàng của từng Seller |
| 17 | `PUT` | [`/api/v1/admin/user/{userId}/roles`](#17-put-api-v1-admin-user-userid-roles) | `Admin` / `SuperAdmin` | Gán / Thu hồi vai trò nội bộ (Phân quyền SoD) |
| 18 | `PATCH` | [`/api/v1/admin/user/{userId}/reputation`](#18-patch-api-v1-admin-user-userid-reputation) | `Admin` / `SuperAdmin` | Admin điều chỉnh uy tín của user(thủ công) |
| 19 | `GET` | [`/api/v1/admin/roles`](#19-get-api-v1-admin-roles) | `Admin` / `SuperAdmin` | Admin xem toàn bộ roles trong hệ thống |
| 20 | `GET` | [`/api/v1/admin/permissions`](#20-get-api-v1-admin-permissions) | `Admin` / `SuperAdmin` | Xem danh sách các đặc quyền |
| 21 | `PUT` | [`/api/v1/admin/roles/{roleId}/permissions`](#21-put-api-v1-admin-roles-roleid-permissions) | `Admin` / `SuperAdmin` | Cập nhật danh sách đặc quyền (Permissions) cho Vai trò |
| 22 | `GET` | [`/api/v1/admin/audit-logs`](#22-get-api-v1-admin-audit-logs) | `Admin` / `SuperAdmin` | Tra cứu Nhật ký Kiểm toán hệ thống |
| 23 | `GET` | [`/api/v1/admin/configs`](#23-get-api-v1-admin-configs) | `Admin` / `SuperAdmin` | Xem danh sách tham số cấu hình toàn sàn |
| 24 | `PUT` | [`/api/v1/admin/configs`](#24-put-api-v1-admin-configs) | `Admin` / `SuperAdmin` | Cập nhật tham số cấu hình toàn sàn |
| 25 | `GET` | [`/api/v1/admin/auction-configs`](#25-get-api-v1-admin-auction-configs) | `Admin` / `SuperAdmin` | Xem các luật đang hiệu lực (tham chiếu tới userSerive để lấy field uy tín seller,buyer, số lần gia hạn,gia hạn bao nhiêu phút,.....) |
| 26 | `PUT` | [`/api/v1/admin/auction-configs/{key}`](#26-put-api-v1-admin-auction-configs-key-) | `Admin` / `SuperAdmin` | Cập nhật cấu hình đấu giá(chỉ ảnh hưởng auction tương lai ) |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-get-api-v1-admin-seller-registrations"></a>
### 1. `GET /api/v1/admin/seller-registrations`

**Danh sách hồ sơ Seller chờ duyệt**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/seller-registrations`

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
  "status": "string (default: PENDING_APPROVAL)"
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
    "message": "Lấy danh sách hồ sơ mở Shop thành công",
    "data": {
      "items": [
        {
          "registrationId": "uuid",
          "userId": "uuid",
          "shopName": "string",
          "identityCardNumber": "string",
          "warehouseAddress": "string",
          "reputationScore": "int"
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
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to view shop registration list. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="2-get-api-v1-admin-seller-registrations-registrationid-"></a>
### 2. `GET /api/v1/admin/seller-registrations/{registrationId}`

**Xem chi tiết hồ sơ mở Shop và tài liệu chứng từ định danh**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/seller-registrations/{registrationId}`

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
  "registrationId": "uuid"
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
    "message": "Lấy chi tiết hồ sơ mở Shop thành công",
    "data": {
      "registrationId": "uuid",
      "userId": "uuid",
      "shopName": "string",
      "hotline": "string",
      "pickupAddress": "string",
      "identityCardNumber": "string",
      "idCardFrontUrl": "string",
      "idCardBackUrl": "string",
      "businessLicenseUrl": "string",
      "reputationScore": "int",
      "status": "string",
      "submittedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to view registration requests. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_REGISTRATION_NOT_FOUND` | Registration request was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="3-put-api-v1-admin-seller-registrations-registrationid-approve"></a>
### 3. `PUT /api/v1/admin/seller-registrations/{registrationId}/approve`

**Staff duyệt hồ sơ Seller**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/seller-registrations/{registrationId}/approve`

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
  { "registrationId": "uuid" }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "note": "string (optional)"
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
    "message": "Hồ sơ mở Shop đã được phê duyệt. Người dùng đã trở thành Seller",
    "data": {
      "registrationId": "uuid",
      "userId": "uuid",
      "status": "APPROVED",
      "approvedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_REQUEST_NOT_PENDING` | Registration request is not in a pending state. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ERR_REPUTATION_TOO_LOW` | Applicant reputation score has fallen below the threshold of 50. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to approve shop registrations. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_REQUEST_NOT_FOUND` | Registration request was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="4-put-api-v1-admin-seller-registrations-registrationid-reject"></a>
### 4. `PUT /api/v1/admin/seller-registrations/{registrationId}/reject`

**Staff từ chối hồ sơ Seller**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/seller-registrations/{registrationId}/reject`

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
  { "registrationId": "uuid" }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "rejectionReason": "string (lý do cụ thể)"
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
    "message": "Đã từ chối hồ sơ mở Shop",
    "data": {
      "registrationId": "uuid",
      "status": "REJECTED",
      "rejectedAt": "timestamp",
      "rejectionReason": "string"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_REASON_REQUIRED` | Rejection reason must be at least 10 characters long. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to reject registration requests. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_REQUEST_NOT_FOUND` | Registration request was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="5-get-api-v1-admin-reports"></a>
### 5. `GET /api/v1/admin/reports`

**Admin/Staff xem danh sách các đơn tố cáo tài khoản**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/reports`

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
    "status": "PENDING|RESOLVED|DISMISSED",
    "violationType": "SPAM|FRAUD"
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
    "message": "Lấy danh sách đơn tố cáo thành công",
    "data": {
      "items": [
        {
          "id": "uuid",
          "reporterId": "uuid",
          "reportedUserId": "uuid",
          "violationType": "string",
          "description": "string",
          "evidenceUrls": "string[]",
          "status": "string",
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
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to moderate reports. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="6-put-api-v1-admin-reports-reportid-resolve"></a>
### 6. `PUT /api/v1/admin/reports/{reportId}/resolve`

**Staff xử lý chấp thuận tố cáo & áp dụng chế tài kỷ luật**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/reports/{reportId}/resolve`

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
  { "reportId": "uuid" }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "action": "string (\"DEDUCT_REPUTATION\" | \"BAN_USER\")",
  "pointsDeducted": "int (optional: 1..50)",
  "resolutionNote": "string (>= 10 ký tự)"
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
    "message": "Đã xử lý tố cáo thành công. Chế tài kỷ luật đã được áp dụng",
    "data": {
      "reportId": "uuid",
      "status": "RESOLVED",
      "resolvedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_ALREADY_RESOLVED` | Report has already been resolved or processed. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to process reports. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_REPORT_NOT_FOUND` | Report was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="7-put-api-v1-admin-reports-reportid-dismiss"></a>
### 7. `PUT /api/v1/admin/reports/{reportId}/dismiss`

**Staff bác bỏ đơn tố cáo sai sự thật / không đủ chứng cứ**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/reports/{reportId}/dismiss`

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
  { "reportId": "uuid" }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "reason": "string (lý do bác bỏ)"
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
    "message": "Đã bác bỏ đơn tố cáo",
    "data": {
      "reportId": "uuid",
      "status": "DISMISSED",
      "dismissedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_REASON_REQUIRED` | A reason is required to dismiss the report. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to dismiss reports. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_REPORT_NOT_FOUND` | Report was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="8-put-api-v1-admin-reviews-id-hide"></a>
### 8. `PUT /api/v1/admin/reviews/{id}/hide`

**Staff/Admin ẩn đánh giá vi phạm tiêu chuẩn**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/reviews/{id}/hide`

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
  { "id": "uuid" }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "reason": "string (lý do ẩn vi phạm)"
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
    "message": "Đã ẩn đánh giá vi phạm thành công",
    "data": {
      "reviewId": "uuid",
      "isHidden": true
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_REASON_REQUIRED` | A reason is required to hide the review. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to moderate reviews. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_REVIEW_NOT_FOUND` | Review was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="9-get-api-v1-admin-user"></a>
### 9. `GET /api/v1/admin/user`

**Admin xem danh sách các user**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/user`

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
  "keyword": "string (tìm tên, email, sđt)",
  "role": "string",
  "status": "string"
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
    "message": "Lấy danh sách người dùng thành công",
    "data": {
      "items": [
        {
          "id": "uuid",
          "email": "string",
          "firstName": "string",
          "lastName": "string",
          "phoneNumber": "string",
          "status": "string",
          "reputationScore": "int",
          "roles": "string[]"
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
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to view user list. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="10-post-api-v1-admin-user"></a>
### 10. `POST /api/v1/admin/user`

**Admin tạo user mới**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/admin/user`

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
  "email": "string",
  "password": "string",
  "firstName": "string",
  "lastName": "string",
  "phoneNumber": "string",
  "roleCodes": [
  "STAFF"
  ]
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
    "message": "Tạo tài khoản thành công",
    "data": {
      "userId": "uuid",
      "email": "string",
      "roles": "string[]"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_INVALID_ROLES` | The specified role list is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to create user accounts. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `409` | `ERR_EMAIL_EXISTS` | Email already exists in the system. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="11-get-api-v1-admin-user-userid-"></a>
### 11. `GET /api/v1/admin/user/{userId}`

**Xem chi tiết hồ sơ cá nhân, phân quyền & uy tín**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/user/{userId}`

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
    "message": "Lấy chi tiết người dùng thành công",
    "data": {
      "id": "uuid",
      "email": "string",
      "firstName": "string",
      "lastName": "string",
      "phoneNumber": "string",
      "status": "string",
      "reputationScore": "int",
      "trustLevel": "string",
      "roles": "string[]",
      "permissions": "string[]",
      "sellerProfile": "object",
      "createdAt": "timestamp",
      "auditHistory": "object[]"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to view user details. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_USER_NOT_FOUND` | User was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="12-put-api-v1-admin-user-userid-"></a>
### 12. `PUT /api/v1/admin/user/{userId}`

**Admin cập nhật thông tin của user cụ thể**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/user/{userId}`

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

- **Request Body (JSON Payload):**
  ```json
  {
  "firstName": "string",
  "lastName": "string",
  "phoneNumber": "string",
  "status": "string",
  "reason": "string (lý do sửa)"
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
    "message": "Cập nhật thông tin người dùng thành công",
    "data": {
      "id": "uuid",
      "firstName": "string",
      "lastName": "string",
      "status": "string",
      "updatedBy": "uuid",
      "updatedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_AUDIT_REASON_REQUIRED` | Intervention reason must be at least 10 characters long. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to edit user information. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_USER_NOT_FOUND` | User was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="13-delete-api-v1-admin-user-userid-"></a>
### 13. `DELETE /api/v1/admin/user/{userId}`

**Xóa MỀM 1 user khỏi hệ thống**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `DELETE`
- **Đường dẫn**: `/api/v1/admin/user/{userId}`

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

- **Request Body (JSON Payload):**
  ```json
  {
  "reason": "string (lý do xóa)"
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
    "message": "Tài khoản người dùng đã được xóa mềm thành công",
    "data": {
      "id": "uuid",
      "isDeleted": true,
      "deletedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_CANNOT_DELETE_ADMIN` | Cannot delete an administrator account. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to delete user accounts. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_USER_NOT_FOUND` | User was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `ERR_ACTIVE_AUCTIONS_EXIST` | Account currently has active auctions or pending orders. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="14-patch-api-v1-admin-user-userid-ban"></a>
### 14. `PATCH /api/v1/admin/user/{userId}/ban`

**Admin ban 1 user**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/admin/user/{userId}/ban`

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

- **Request Body (JSON Payload):**
  ```json
  {
  "reason": "string (lý do khóa)",
  "banDurationDays": "int (0 = vĩnh viễn)"
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
    "message": "Đã khóa tài khoản người dùng thành công",
    "data": {
      "id": "uuid",
      "status": "BANNED",
      "bannedUntil": "timestamp",
      "reason": "string"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_CANNOT_BAN_ADMIN` | Cannot ban or lock an administrator account. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ERR_ALREADY_BANNED` | Account is already banned or locked. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to ban users. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_USER_NOT_FOUND` | User was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="15-patch-api-v1-admin-user-userid-unban"></a>
### 15. `PATCH /api/v1/admin/user/{userId}/unban`

**Admin hủy ban 1 user**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/admin/user/{userId}/unban`

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

- **Request Body (JSON Payload):**
  ```json
  {
  "reason": "string (lý do mở khóa)"
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
    "message": "Đã mở khóa tài khoản người dùng thành công",
    "data": {
      "id": "uuid",
      "status": "ACTIVE",
      "unbannedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_USER_NOT_BANNED` | Account is currently active and not banned. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to unban users. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_USER_NOT_FOUND` | User was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="16-get-api-v1-admin-sellers-sellerid-"></a>
### 16. `GET /api/v1/admin/sellers/{sellerId}`

**Admin lấy thông tin cửa hàng của từng Seller**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/sellers/{sellerId}`

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
    "message": "Lấy chi tiết Shop thành công",
    "data": {
      "sellerId": "uuid",
      "shopName": "string",
      "ownerUserId": "uuid",
      "identityCardNumber": "string",
      "pickupAddress": "string",
      "status": "string",
      "auditHistory": "object[]"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_FORBIDDEN` | Administrator privileges are required. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_SELLER_NOT_FOUND` | Shop information was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="17-put-api-v1-admin-user-userid-roles"></a>
### 17. `PUT /api/v1/admin/user/{userId}/roles`

**Gán / Thu hồi vai trò nội bộ (Phân quyền SoD)**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/user/{userId}/roles`

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

- **Request Body (JSON Payload):**
  ```json
  {
    "roleCodes": ["Staff"],
    "roleIds": [1],
    "reason": "Promotion to customer support staff."
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
    "message": "Cập nhật vai trò người dùng thành công",
    "data": {
      "userId": "uuid",
      "roles": [
        "STAFF"
      ],
      "updatedBy": "uuid"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_SOD_VIOLATION` | Separation of duties (SoD) policy violation detected. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ERR_CANNOT_DEMOTE_LAST_ADMIN` | Cannot demote the last remaining administrator. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_FORBIDDEN` | Only super admin has permission to assign roles. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="18-patch-api-v1-admin-user-userid-reputation"></a>
### 18. `PATCH /api/v1/admin/user/{userId}/reputation`

**Admin điều chỉnh uy tín của user(thủ công)**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/admin/user/{userId}/reputation`

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

- **Request Body (JSON Payload):**
  ```json
  {
  "delta": "int ([-50..+50])",
  "reason": "string (>= 10 ký tự)"
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
    "message": "Điều chỉnh điểm uy tín người dùng thành công",
    "data": {
      "userId": "uuid",
      "previousScore": "int",
      "newScore": "int",
      "delta": "int",
      "trustLevel": "string"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_REASON_REQUIRED` | Reputation adjustment reason must be at least 10 characters long. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `ERR_INVALID_DELTA` | Reputation delta must be non-zero and within [-50, +50]. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to adjust reputation scores. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_USER_NOT_FOUND` | User was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="19-get-api-v1-admin-roles"></a>
### 19. `GET /api/v1/admin/roles`

**Admin xem toàn bộ roles trong hệ thống**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/roles`

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
    "message": "Lấy danh mục vai trò thành công",
    "data": {
      "items": [
        {
          "roleId": "int",
          "roleName": "string",
          "description": "string",
          "isSystemRole": "boolean",
          "permissionsCount": "int"
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
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to view roles. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="20-get-api-v1-admin-permissions"></a>
### 20. `GET /api/v1/admin/permissions`

**Xem danh sách các đặc quyền**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/permissions`

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
    "message": "Lấy danh mục đặc quyền thành công",
    "data": {
      "items": [
        {
          "permissionId": "int",
          "permissionCode": "string",
          "module": "string",
          "description": "string"
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
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to view permissions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="21-put-api-v1-admin-roles-roleid-permissions"></a>
### 21. `PUT /api/v1/admin/roles/{roleId}/permissions`

**Cập nhật danh sách đặc quyền (Permissions) cho Vai trò**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/roles/{roleId}/permissions`

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
  { "roleId": 1 }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "description": "Customer support and complaint resolution.",
    "permissionIds": [1, 2, 3]
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
    "message": "Cập nhật quyền hạn vai trò thành công",
    "data": {
      "roleId": "int",
      "updatedPermissionsCount": "int"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_CANNOT_EDIT_SYSTEM_ROLE` | System-defined roles cannot be edited or modified. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to edit roles. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_ROLE_NOT_FOUND` | Specified role does not exist. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="22-get-api-v1-admin-audit-logs"></a>
### 22. `GET /api/v1/admin/audit-logs`

**Tra cứu Nhật ký Kiểm toán hệ thống**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/audit-logs`

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
    "actorId": "uuid",
    "action": "string",
    "fromDate": "2026-10-01T00:00:00Z",
    "toDate": "2026-10-26T00:00:00Z"
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
          "actorId": "uuid",
          "action": "USER_BANNED",
          "targetEntity": "users",
          "oldValues": {},
          "newValues": {},
          "reason": "Policy violation.",
          "ipAddress": "127.0.0.1",
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
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | User does not have permission to view audit logs. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="23-get-api-v1-admin-configs"></a>
### 23. `GET /api/v1/admin/configs`

**Xem danh sách tham số cấu hình toàn sàn**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/configs`

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
    "message": "Lấy cấu hình hệ thống thành công",
    "data": {
      "items": [
        {
          "key": "string",
          "value": "string",
          "description": "string",
          "dataType": "string",
          "updatedAt": "timestamp"
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
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to view system configurations. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="24-put-api-v1-admin-configs"></a>
### 24. `PUT /api/v1/admin/configs`

**Cập nhật tham số cấu hình toàn sàn**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/configs`

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
  "key": "string",
  "value": "string",
  "reason": "string"
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
    "message": "Cập nhật cấu hình sàn thành công",
    "data": {
      "key": "string",
      "oldValue": "string",
      "newValue": "string",
      "updatedAt": "timestamp"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `ERR_INVALID_CONFIG_VALUE` | Configuration parameter value is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `ERR_UNAUTHORIZED` | Authentication required, user is not logged in. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `ERR_PERMISSION_DENIED` | You do not have permission to edit system configurations. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `ERR_CONFIG_NOT_FOUND` | Configuration key was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="25-get-api-v1-admin-auction-configs"></a>
### 25. `GET /api/v1/admin/auction-configs`

**Xem các luật đang hiệu lực (tham chiếu tới userSerive để lấy field uy tín seller,buyer, số lần gia hạn,gia hạn bao nhiêu phút,.....)**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/admin/auction-configs`
- **Mã tham chiếu Excel**: `AUC-16`

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
          "configKey": "string",
          "configValue": "json",
          "description": "string | null",
          "version": "int",
          "updatedBy": "UUID | null",
          "updatedAt": "datetime"
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
| `403` | `FORBIDDEN` | Only administrators can perform this action. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="26-put-api-v1-admin-auction-configs-key-"></a>
### 26. `PUT /api/v1/admin/auction-configs/{key}`

**Cập nhật cấu hình đấu giá(chỉ ảnh hưởng auction tương lai )**

- **Quyền hạn (RBAC)**: `Admin` / `SuperAdmin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/admin/auction-configs/{key}`
- **Mã tham chiếu Excel**: `AUC-17`

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
  { "key": "string" }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "configValue": "json",
  "description": "string (optional)"
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
      "configKey": "string",
      "configValue": "json",
      "description": "string | null",
      "version": "int",
      "isActive": "boolean",
      "updatedBy": "UUID | null",
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
| `400` | `VALIDATION_ERROR` | Configuration value type is invalid or out of allowed bounds for this key. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only administrators can perform this action. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `NOT_FOUND` | Configuration key was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `CONFLICT` | Update conflict, only one active record is permitted per key. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---
