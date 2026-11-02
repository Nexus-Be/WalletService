# 11 — TÀI LIỆU API ĐIỀU KHIỂN GATEWAY, MOCK ENGINE & HEALTH PROBER (GATEWAY CONTROL & HEALTH API)

> **Dịch vụ phụ trách:** `ApiGateway (Edge Proxy, Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `17 endpoints`  
> **Mục đích:** Cung cấp cho đội ngũ Frontend các endpoint kỹ thuật đặc biệt để:
> 1. Bật/tắt chế độ Mock Data theo từng microservice mà không cần backend thật chạy.
> 2. Bật chế độ Auth Bypass (tự động đăng nhập mà không cần JWT token thật trong môi trường dev).
> 3. Tải lại (Hot-reload) dữ liệu mock khi sửa đổi các file JSON trong `Seeds/`.
> 4. Kiểm tra sức khỏe hệ thống (Health Check) của Gateway, 8 Microservices và 8 Database PostgreSQL.

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `GET` | [`/api/v1/gateway/modes`](#1-get-api-v1-gateway-modes) | `Admin` / `Dev` | Lấy trạng thái cấu hình Mock/Real và AuthMode hiện tại |
| 2 | `PUT` | [`/api/v1/gateway/mode`](#2-put-api-v1-gateway-mode) | `Admin` / `Dev` | Cập nhật chế độ xác thực Gateway (`Enforce` hoặc `Bypass`) |
| 3 | `PUT` | [`/api/v1/gateway/modes/{service}`](#3-put-api-v1-gateway-modes-service) | `Admin` / `Dev` | Chuyển đổi chế độ `Mock` hoặc `Real` cho một service cụ thể |
| 4 | `PUT` | [`/api/v1/gateway/modes`](#4-put-api-v1-gateway-modes) | `Admin` / `Dev` | Cập nhật hàng loạt (Bulk) chế độ của Gateway và các service |
| 5 | `GET` | [`/api/v1/mock-engine/definitions`](#5-get-api-v1-mock-engine-definitions) | `Admin` / `Dev` | Tra cứu danh mục các route mock và seed JSON fixture tương ứng |
| 6 | `POST` | [`/api/v1/mock-engine/reload`](#6-post-api-v1-mock-engine-reload) | `Admin` / `Dev` | Tải lại (Hot-reload) toàn bộ dữ liệu mock trong `Seeds/` |
| 7 | `GET` | [`/api/v1/gateway/prober/status`](#7-get-api-v1-gateway-prober-status) | `Admin` / `Dev` | Kiểm tra tổng hợp trạng thái sức khỏe của 8 Microservices |
| 8 | `GET` | [`/api/v1/gateway/prober/status/{service}`](#8-get-api-v1-gateway-prober-status-service) | `Admin` / `Dev` | Kiểm tra trạng thái sức khỏe của một microservice cụ thể |
| 9 | `PUT` | [`/api/v1/gateway/prober/{service}`](#9-put-api-v1-gateway-prober-service) | `Admin` / `Dev` | Ghi đè trạng thái sức khỏe của một microservice |
| 10 | `PUT` | [`/api/v1/gateway/prober/bulk`](#10-put-api-v1-gateway-prober-bulk) | `Admin` / `Dev` | Ghi đè hàng loạt trạng thái sức khỏe cho nhiều service |
| 11 | `POST` | [`/api/v1/gateway/prober/{service}/check`](#11-post-api-v1-gateway-prober-service-check) | `Admin` / `Dev` | Kích hoạt kiểm tra trực tiếp tức thì (Live Probe) tới service |
| 12 | `GET` | [`/api/v1/gateway/prober/databases/status`](#12-get-api-v1-gateway-prober-databases-status) | `Admin` / `Dev` | Kiểm tra trạng thái kết nối tới 8 Database PostgreSQL |
| 13 | `GET` | [`/api/v1/gateway/prober/databases/status/{service}`](#13-get-api-v1-gateway-prober-databases-status-service) | `Admin` / `Dev` | Kiểm tra trạng thái kết nối Database của một service |
| 14 | `PUT` | [`/api/v1/gateway/prober/databases/{service}`](#14-put-api-v1-gateway-prober-databases-service) | `Admin` / `Dev` | Ghi đè trạng thái sức khỏe của một database |
| 15 | `POST` | [`/api/v1/gateway/prober/databases/{service}/check`](#15-post-api-v1-gateway-prober-databases-service-check) | `Admin` / `Dev` | Kích hoạt kiểm tra kết nối trực tiếp tức thì tới PostgreSQL |
| 16 | `GET` | [`/api/v1/gateway/prober/healthcheck-service/status`](#16-get-api-v1-gateway-prober-healthcheck-service-status) | `Admin` / `Dev` | Kiểm tra trạng thái dịch vụ giám sát tự phục hồi (CheckHeal) |
| 17 | `POST` | [`/api/v1/gateway/prober/healthcheck-service/check`](#17-post-api-v1-gateway-prober-healthcheck-service-check) | `Admin` / `Dev` | Kích hoạt kiểm tra kết nối tới dịch vụ CheckHeal |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-get-api-v1-gateway-modes"></a>
### 1. `GET /api/v1/gateway/modes`
**Lấy trạng thái cấu hình Mock/Real và AuthMode hiện tại của API Gateway**

- **Quyền hạn (RBAC)**: `Admin` (Trong môi trường Dev có thể cấu hình bypass hoặc truyền token admin)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/gateway/modes`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Authorization: Bearer <ADMIN_JWT_TOKEN>
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Cấu trúc Envelope:** `ApiResponse<GatewayStateResponseDto>`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Gateway service modes retrieved.",
    "data": {
      "authMode": "Enforce",
      "services": {
        "users": "Mock",
        "catalog": "Mock",
        "commerce": "Mock",
        "auction": "Mock",
        "fulfillment": "Mock",
        "notification": "Mock",
        "wallet": "Mock",
        "log": "Mock"
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="2-put-api-v1-gateway-mode"></a>
### 2. `PUT /api/v1/gateway/mode`
**Cập nhật chế độ xác thực toàn cục của API Gateway (`Enforce` hoặc `Bypass`)**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/gateway/mode`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <ADMIN_JWT_TOKEN>
  ```
- **Request Body (JSON Payload):**
  ```json
  {
    "mode": "Bypass"
  }
  ```
  *(Ghi chú: Giá trị hợp lệ là `"Enforce"` hoặc `"Bypass"`. Chế độ `Bypass` chỉ được phép kích hoạt trong môi trường Development).*

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Gateway auth mode updated to 'Bypass'.",
    "data": {
      "authMode": "Bypass"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="3-put-api-v1-gateway-modes-service"></a>
### 3. `PUT /api/v1/gateway/modes/{service}`
**Chuyển đổi chế độ Mock hoặc Real cho một dịch vụ backend cụ thể**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/gateway/modes/{service}`

#### 1. Yêu cầu (Request)
- **Path Parameters:**
  | Tham số | Kiểu | Bắt buộc | Mô tả |
  | :--- | :--- | :---: | :--- |
  | `service` | `string` | Có | Tên service: `users`, `catalog`, `commerce`, `auction`, `fulfillment`, `notification`, `wallet`, `log` |
- **Request Body (JSON Payload):**
  ```json
  {
    "mode": "Real"
  }
  ```
  *(Ghi chú: Giá trị hợp lệ là `"Mock"` hoặc `"Real"`).*

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Service 'catalog' mode updated to 'Real'.",
    "data": {
      "service": "catalog",
      "mode": "Real"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="4-put-api-v1-gateway-modes"></a>
### 4. `PUT /api/v1/gateway/modes`
**Cập nhật hàng loạt (Bulk) chế độ của Gateway và các microservice**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/gateway/modes`

#### 1. Yêu cầu (Request)
- **Request Body (JSON Payload):**
  ```json
  {
    "authMode": "Enforce",
    "services": {
      "users": "Real",
      "catalog": "Real",
      "commerce": "Mock",
      "auction": "Mock",
      "fulfillment": "Mock",
      "notification": "Mock",
      "wallet": "Mock",
      "log": "Mock"
    }
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Gateway bulk modes updated successfully.",
    "data": {
      "authMode": "Enforce",
      "services": {
        "users": "Real",
        "catalog": "Real",
        "commerce": "Mock",
        "auction": "Mock",
        "fulfillment": "Mock",
        "notification": "Mock",
        "wallet": "Mock",
        "log": "Mock"
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="5-get-api-v1-mock-engine-definitions"></a>
### 5. `GET /api/v1/mock-engine/definitions`
**Tra cứu danh mục toàn bộ các route mock và seed JSON fixture tương ứng**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/mock-engine/definitions`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Authorization: Bearer <ADMIN_JWT_TOKEN>
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Mock engine definitions retrieved.",
    "data": {
      "totalRoutes": 114,
      "routes": [
        {
          "method": "POST",
          "route": "/api/v1/auth/login",
          "service": "users",
          "seedPath": "Seeds/auth/login.json"
        },
        {
          "method": "GET",
          "route": "/api/v1/products",
          "service": "catalog",
          "seedPath": "Seeds/catalog/get-products.json"
        }
      ]
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="6-post-api-v1-mock-engine-reload"></a>
### 6. `POST /api/v1/mock-engine/reload`
**Tải lại (Hot-reload) toàn bộ dữ liệu mock trong `Seeds/` mà không cần khởi động lại Gateway**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/mock-engine/reload`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Authorization: Bearer <ADMIN_JWT_TOKEN>
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Mock engine fixtures reloaded successfully from disk.",
    "data": {
      "reloadedFilesCount": 107,
      "reloadedTimestamp": "2026-10-27T08:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="7-get-api-v1-gateway-prober-status"></a>
### 7. `GET /api/v1/gateway/prober/status`
**Kiểm tra tổng hợp trạng thái sức khỏe hoạt động của toàn bộ 8 Microservices backend**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/gateway/prober/status`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Authorization: Bearer <ADMIN_JWT_TOKEN>
  # Hoặc Header API Key:
  x-api-key: <CHECKHEAL_API_KEY>
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Service health statuses retrieved.",
    "data": {
      "overallStatus": "Healthy",
      "services": {
        "users": { "status": "Healthy", "port": 8001, "lastCheck": "2026-10-27T08:00:00Z", "latencyMs": 12 },
        "catalog": { "status": "Healthy", "port": 8002, "lastCheck": "2026-10-27T08:00:00Z", "latencyMs": 8 },
        "commerce": { "status": "Healthy", "port": 8003, "lastCheck": "2026-10-27T08:00:00Z", "latencyMs": 15 },
        "auction": { "status": "Healthy", "port": 8005, "lastCheck": "2026-10-27T08:00:00Z", "latencyMs": 10 },
        "fulfillment": { "status": "Healthy", "port": 8006, "lastCheck": "2026-10-27T08:00:00Z", "latencyMs": 9 },
        "notification": { "status": "Healthy", "port": 8007, "lastCheck": "2026-10-27T08:00:00Z", "latencyMs": 11 },
        "wallet": { "status": "Healthy", "port": 8008, "lastCheck": "2026-10-27T08:00:00Z", "latencyMs": 14 },
        "log": { "status": "Healthy", "port": 8004, "lastCheck": "2026-10-27T08:00:00Z", "latencyMs": 7 }
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="8-get-api-v1-gateway-prober-status-service"></a>
### 8. `GET /api/v1/gateway/prober/status/{service}`
**Kiểm tra trạng thái sức khỏe của một microservice cụ thể**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/gateway/prober/status/{service}`

#### 1. Yêu cầu (Request)
- **Path Parameters:**
  | Tham số | Kiểu | Bắt buộc | Mô tả |
  | :--- | :--- | :---: | :--- |
  | `service` | `string` | Có | Tên service: `users`, `catalog`, `commerce`, `auction`, `fulfillment`, `notification`, `wallet`, `log` |

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Service 'users' health status retrieved.",
    "data": {
      "service": "users",
      "status": "Healthy",
      "port": 8001,
      "lastCheck": "2026-10-27T08:00:00Z",
      "latencyMs": 12
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="9-put-api-v1-gateway-prober-service"></a>
### 9. `PUT /api/v1/gateway/prober/{service}`
**Ghi đè thủ công trạng thái sức khỏe của một microservice**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/gateway/prober/{service}`

#### 1. Yêu cầu (Request)
- **Request Body (JSON Payload):**
  ```json
  {
    "status": "Degraded"
  }
  ```
  *(Giá trị trạng thái hợp lệ: `"Healthy"`, `"Degraded"`, `"Unhealthy"`).*

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Health status of 'users' overridden to 'Degraded'.",
    "data": {
      "service": "users",
      "status": "Degraded"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="10-put-api-v1-gateway-prober-bulk"></a>
### 10. `PUT /api/v1/gateway/prober/bulk`
**Ghi đè hàng loạt trạng thái sức khỏe cho nhiều microservice cùng lúc**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/gateway/prober/bulk`

#### 1. Yêu cầu (Request)
- **Request Body (JSON Payload):**
  ```json
  {
    "statuses": {
      "users": "Healthy",
      "catalog": "Healthy",
      "commerce": "Healthy"
    }
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Bulk health statuses updated.",
    "data": {
      "updatedCount": 3
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="11-post-api-v1-gateway-prober-service-check"></a>
### 11. `POST /api/v1/gateway/prober/{service}/check`
**Kích hoạt kiểm tra sức khỏe trực tiếp tức thì (Live HTTP Probe) tới cổng của microservice**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/gateway/prober/{service}/check`

#### 1. Yêu cầu (Request)
- **Path Parameters:**
  | Tham số | Kiểu | Bắt buộc | Mô tả |
  | :--- | :--- | :---: | :--- |
  | `service` | `string` | Có | Tên service: `users`, `catalog`, `commerce`, `auction`, `fulfillment`, `notification`, `wallet`, `log` |

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Live health probe completed for 'catalog'.",
    "data": {
      "service": "catalog",
      "status": "Healthy",
      "httpStatusCode": 200,
      "latencyMs": 14,
      "endpoint": "http://localhost:8002/health"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="12-get-api-v1-gateway-prober-databases-status"></a>
### 12. `GET /api/v1/gateway/prober/databases/status`
**Kiểm tra trạng thái kết nối tới 8 Database PostgreSQL backend (`5433`-`5440`)**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/gateway/prober/databases/status`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Authorization: Bearer <ADMIN_JWT_TOKEN>
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Database health statuses retrieved.",
    "data": {
      "overallDatabaseStatus": "Healthy",
      "databases": {
        "users": { "status": "Healthy", "port": 5433, "database": "nexus_user", "latencyMs": 4 },
        "catalog": { "status": "Healthy", "port": 5434, "database": "nexus_catalog", "latencyMs": 3 },
        "commerce": { "status": "Healthy", "port": 5435, "database": "nexus_commerce", "latencyMs": 5 },
        "auction": { "status": "Healthy", "port": 5436, "database": "nexus_auction", "latencyMs": 4 },
        "fulfillment": { "status": "Healthy", "port": 5437, "database": "nexus_fulfillment", "latencyMs": 3 },
        "notification": { "status": "Healthy", "port": 5438, "database": "nexus_notification", "latencyMs": 4 },
        "wallet": { "status": "Healthy", "port": 5439, "database": "nexus_wallet", "latencyMs": 6 },
        "log": { "status": "Healthy", "port": 5440, "database": "nexus_log", "latencyMs": 3 }
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="13-get-api-v1-gateway-prober-databases-status-service"></a>
### 13. `GET /api/v1/gateway/prober/databases/status/{service}`
**Kiểm tra trạng thái kết nối database của một microservice cụ thể**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/gateway/prober/databases/status/{service}`

#### 1. Yêu cầu (Request)
- **Path Parameters:**
  | Tham số | Kiểu | Bắt buộc | Mô tả |
  | :--- | :--- | :---: | :--- |
  | `service` | `string` | Có | Tên service: `users`, `catalog`, `commerce`, `auction`, `fulfillment`, `notification`, `wallet`, `log` |

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Database status of 'users' retrieved.",
    "data": {
      "service": "users",
      "status": "Healthy",
      "port": 5433,
      "database": "nexus_user",
      "latencyMs": 4
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="14-put-api-v1-gateway-prober-databases-service"></a>
### 14. `PUT /api/v1/gateway/prober/databases/{service}`
**Ghi đè thủ công trạng thái sức khỏe của một database**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/gateway/prober/databases/{service}`

#### 1. Yêu cầu (Request)
- **Request Body (JSON Payload):**
  ```json
  {
    "status": "Degraded"
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Database health status of 'users' overridden to 'Degraded'.",
    "data": {
      "service": "users",
      "status": "Degraded"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="15-post-api-v1-gateway-prober-databases-service-check"></a>
### 15. `POST /api/v1/gateway/prober/databases/{service}/check`
**Kích hoạt kiểm tra kết nối trực tiếp tức thì tới PostgreSQL của một service**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/gateway/prober/databases/{service}/check`

#### 1. Yêu cầu (Request)
- **Path Parameters:**
  | Tham số | Kiểu | Bắt buộc | Mô tả |
  | :--- | :--- | :---: | :--- |
  | `service` | `string` | Có | Tên service: `users`, `catalog`, `commerce`, `auction`, `fulfillment`, `notification`, `wallet`, `log` |

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Direct PostgreSQL connection test passed for 'catalog'.",
    "data": {
      "service": "catalog",
      "port": 5434,
      "database": "nexus_catalog",
      "connected": true,
      "latencyMs": 3
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="16-get-api-v1-gateway-prober-healthcheck-service-status"></a>
### 16. `GET /api/v1/gateway/prober/healthcheck-service/status`
**Kiểm tra trạng thái kết nối tới dịch vụ giám sát tự phục hồi (CheckHeal daemon)**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/gateway/prober/healthcheck-service/status`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Authorization: Bearer <ADMIN_JWT_TOKEN>
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "CheckHeal service connection status retrieved.",
    "data": {
      "connected": true,
      "serviceUrl": "http://localhost:5050",
      "status": "Online",
      "lastHeartbeat": "2026-10-27T08:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

<a id="17-post-api-v1-gateway-prober-healthcheck-service-check"></a>
### 17. `POST /api/v1/gateway/prober/healthcheck-service/check`
**Kích hoạt kiểm tra kết nối trực tiếp tức thì tới dịch vụ CheckHeal**

- **Quyền hạn (RBAC)**: `Admin`
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/gateway/prober/healthcheck-service/check`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Authorization: Bearer <ADMIN_JWT_TOKEN>
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `200 OK`
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 200,
    "message": "Live probe to CheckHeal service succeeded.",
    "data": {
      "online": true,
      "latencyMs": 5,
      "daemonVersion": "1.0.0"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-27T08:00:00Z"
  }
  ```

---

## 4. BẢNG MÃ LỖI GATEWAY ĐẶC THÙ (RFC 9110)

| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `INVALID_AUTH_MODE` | Invalid auth mode. Must be 'Enforce' or Development-only 'Bypass'. | Chọn lại chế độ `Enforce` hoặc `Bypass`. |
| `400` | `INVALID_MODE` | Invalid service mode. Must be 'Mock' or 'Real'. | Chọn lại chế độ `Mock` hoặc `Real`. |
| `401` | `UNAUTHORIZED` | Invalid or missing CheckHeal API key. | Kiểm tra lại header `Authorization` hoặc `x-api-key`. |
| `403` | `FORBIDDEN` | Access denied. Gateway administration policy required. | Tài khoản phải có quyền Quản trị Gateway (`GatewayAdmin`). |
| `502` | `BAD_GATEWAY` | Downstream service unreachable or returned invalid response. | Service backend đang tắt hoặc crash, bật sang Mock mode để tiếp tục test FE. |
| `504` | `GATEWAY_TIMEOUT` | Downstream service did not respond within configured timeout. | Service backend phản hồi quá lâu (>10 giây). |
