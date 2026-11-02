# 🌐 NEXUS E-COMMERCE ECOSYSTEM — FRONTEND API REFERENCE & INTEGRATION MANUAL

> **Dành cho:** Đội ngũ Frontend Developers (Web, Mobile App, Admin Dashboard)  
> **Cổng truy cập duy nhất (Single Gateway Entrypoint):** `http://localhost:8080`  
> **Giao thức:** 100% RESTful HTTP/JSON (Chuẩn RFC 9110)  
> **Interactive Swagger UI:** [http://localhost:8080/swagger](http://localhost:8080/swagger)  
> **Tổng số API đã chuẩn hóa:** `174 endpoints`  
> **Trạng thái tài liệu:** ✅ Nguồn sự thật đã đối soát giữa Bản đặc tả Excel, Gateway Controllers, Fixture Seeds và Backend Microservices.

> **Trạng thái trong repository hiện tại:** Tài liệu API đã được phân loại theo trạng thái triển khai. Xem [A_ACTIVE](./A_ACTIVE/09_WALLET_PAYMENTS_API.md) cho API đã xác minh trong code; xem [B_NONE](./B_NONE/README.md) cho API chưa hoàn thiện hoặc chưa thể xác minh trong repository này.

---

## 📑 MỤC LỤC TỔNG QUAN

1. [Kiến Trúc & Quy Tắc Tích Hợp Dành Cho Frontend](#1-kiến-trúc--quy-tắc-tích-hợp-dành-cho-frontend)
2. [Cơ Chế Mock Data & Auth Bypass Khi Lập Trình FE](#2-cơ-chế-mock-data--auth-bypass-khi-lập-trình-fe)
3. [Quy Chuẩn Định Dạng Request / Response Envelope](#3-quy-chuẩn-định-dạng-request--response-envelope)
4. [Quy Chuẩn Bắt Lỗi & Xử Lý Ngoại Lệ (Error Handling)](#4-quy-chuẩn-bắt-lỗi--xử-lý-ngoại-lệ-error-handling)
5. [Hệ Thống Phân Quyền (RBAC Matrix)](#5-hệ-thống-phân-quyền-rbac-matrix)
6. [DANH MỤC 11 MODULE API CHI TIẾT (MASTER CATALOG)](#6-danh-mục-11-module-api-chi-tiết-master-catalog)

---

## 1. KIẾN TRÚC & QUY TẮC TÍCH HỢP DÀNH CHO FRONTEND

Hệ thống Nexus được xây dựng theo kiến trúc **Microservices phân tán** với **API Gateway duy nhất (Port 8080)** làm cầu nối trung gian bảo vệ và điều phối:

```
[ Frontend Client (Web / Mobile App / Admin) ]
                     │  (HTTP/JSON)
                     ▼
       ┌───────────────────────────┐
       │   API Gateway (:8080)     │
       │   - Reverse Proxy         │
       │   - JWT Auth & RBAC       │
       │   - Mock Engine (Seeds/)  │
       │   - Rate Limiting         │
       └─────────────┬─────────────┘
                     │ (Forward REST HTTP/JSON)
     ┌───────────────┼───────────────┬───────────────┐
     ▼               ▼               ▼               ▼
[ UserService ] [CatalogService][CommerceService][AuctionService]
  (:8001)         (:8002)         (:8003)         (:8005)
     ▼               ▼               ▼               ▼
[Fulfillment]   [Notification]   [WalletService]  [LogService]
  (:8006)         (:8007)         (:8008)         (:8004)
```

### 3 Nguyên tắc bất biến khi FE tích hợp:
1. **Frontend CHỈ gọi duy nhất vào Gateway Port 8080**: Tuyệt đối không gọi trực tiếp vào các cổng `8001`-`8008` của microservices nội bộ.
2. **100% Giao tiếp qua RESTful HTTP/JSON**:
   - Sử dụng chuẩn HTTP Methods: `GET` (đọc), `POST` (tạo mới/hành động), `PUT` (thay thế toàn bộ), `PATCH` (cập nhật từng phần), `DELETE` (xóa).
   - Đường dẫn chuẩn hóa: `/api/v1/{resource}`.
3. **Quy tắc đặt tên trường dữ liệu (Casing)**:
   - Request Body & Query Params: `camelCase` (ví dụ: `pageSize`, `productId`, `shippingAddress`).
   - Response Payload: `camelCase` (ví dụ: `accessToken`, `totalCount`, `isDefault`).
   - Enum & Status: Viết hoa `SCREAMING_SNAKE_CASE` hoặc `PascalCase` theo đặc tả từng module (ví dụ: `PENDING`, `ACTIVE`, `IN_TRANSIT`, `Buyer`, `Seller`).

---

## 2. CƠ CHẾ MOCK DATA & AUTH BYPASS KHI LẬP TRÌNH FE

API Gateway cung cấp 2 tính năng đặc biệt giúp Frontend **phát triển giao diện hoàn toàn độc lập**, không cần đợi Backend hoàn thiện database hay khởi chạy toàn bộ 8 microservices:

### 2.1 Ép buộc Mock Data bằng Header `x-mock: true`
- Trên bất kỳ request nào, Frontend có thể đính kèm Header:
  ```http
  x-mock: true
  ```
- **Tác dụng**: Gateway sẽ bỏ qua việc gọi service backend thật và lập tức trả về dữ liệu mẫu (Seed Data) chuẩn xác từ thư mục `Seeds/{service}/{fixture}.json` với đầy đủ cấu trúc DTO.

### 2.2 Bật/Tắt Mock Data theo từng Service qua API
- Kiểm tra danh sách trạng thái các service: `GET /api/v1/gateway/modes`
- Bật sang Mock cho service bất kỳ:
  ```http
  PUT /api/v1/gateway/modes/catalog
  Content-Type: application/json

  { "mode": "Mock" }
  ```
- Chuyển toàn bộ 8 service sang Mock:
  ```http
  PUT /api/v1/gateway/mode
  Content-Type: application/json

  { "mode": "all-mock" }
  ```

### 2.3 Chế độ Bỏ qua Xác thực (Auth Bypass - Chỉ dùng trong Local Dev)
- Khi Gateway chạy ở chế độ `AuthMode: Bypass`, FE không cần gửi Bearer Token thật.
- Có thể giả lập người dùng bất kỳ bằng cách truyền Headers:
  ```http
  x-user-id: 11111111-1111-1111-1111-000000000004
  x-user-roles: Buyer
  ```
- Gateway sẽ tự động tin cậy và gán quyền cho request đó.

---

## 3. QUY CHUẨN ĐỊNH DẠNG REQUEST / RESPONSE ENVELOPE

Mọi phản hồi từ Gateway đều được bọc trong một **Vỏ bọc chuẩn hóa (Standard Envelope)**:

### 3.1 Đối tượng đơn lẻ (`ApiResponse<T>`)
Áp dụng cho các API trả về 1 object (Profile, Detail, Create, Update...):

```typescript
interface ApiResponse<T> {
  success: boolean;       // Luôn là true nếu HTTP Status 2xx
  statusCode: number;     // Mã HTTP (200, 201...)
  message: string;        // Thông điệp tiếng Anh chuẩn Sentence case
  data: T;                // Dữ liệu nghiệp vụ trả về
  traceId: string;        // Mã trace chuẩn W3C Distributed Tracing
  timestamp: string;      // Thời gian server (ISO 8601 UTC)
}
```

**Ví dụ JSON thực tế:**
```json
{
  "success": true,
  "statusCode": 200,
  "message": "User profile retrieved successfully.",
  "data": {
    "id": "11111111-1111-1111-1111-000000000004",
    "email": "buyer01@mcs.local",
    "username": "buyer01",
    "displayName": "Bao Nguyen"
  },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "timestamp": "2026-10-27T08:00:00Z"
}
```

### 3.2 Danh sách có phân trang (`ApiResponse<PagedData<T>>`)
Áp dụng cho các API danh sách (Products, Orders, Transactions, Notifications...):

```typescript
interface PagedData<T> {
  items: T[];             // Mảng các bản ghi
  pagination: {
    pageIndex: number;    // Trang hiện tại (bắt đầu từ 1)
    pageSize: number;     // Số phần tử trên 1 trang
    totalCount: number;   // Tổng số bản ghi thỏa mãn điều kiện
    totalPages: number;   // Tổng số trang
    hasPreviousPage: boolean; // Có trang trước không
    hasNextPage: boolean;     // Có trang kế tiếp không
  };
}
```

**Ví dụ JSON thực tế:**
```json
{
  "success": true,
  "statusCode": 200,
  "message": "Products retrieved successfully.",
  "data": {
    "items": [
      {
        "productId": "99999999-9999-9999-9999-000000000001",
        "name": "Sony PlayStation 5 Slim",
        "price": 12500000,
        "rating": 4.8
      }
    ],
    "pagination": {
      "pageIndex": 1,
      "pageSize": 10,
      "totalCount": 85,
      "totalPages": 9,
      "hasPreviousPage": false,
      "hasNextPage": true
    }
  },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "timestamp": "2026-10-27T08:00:00Z"
}
```

### 3.3 Thao tác không trả về dữ liệu (`204 No Content`)
- Các thao tác như `DELETE` hoặc `UPDATE` nhanh không cần trả dữ liệu sẽ trả về mã HTTP `204 No Content` với Body rỗng.

---

## 4. QUY CHUẨN BẮT LỖI & XỬ LÝ NGOẠI LỆ (ERROR HANDLING)

Toàn bộ lỗi từ hệ thống Nexus đều tuân thủ chặt chẽ tiêu chuẩn **RFC 9110 ProblemDetails**:

```typescript
interface ApiErrorResponse {
  type: string;           // URL tham chiếu chuẩn RFC hoặc mã lỗi
  title: string;          // Tiêu đề ngắn gọn của lỗi
  status: number;         // Mã HTTP Status (400, 401, 403, 404, 409, 429, 500)
  errorCode: string;      // Mã lỗi định dạng SCREAMING_SNAKE_CASE
  detail: string;         // Câu giải thích lỗi 100% tiếng Anh, Sentence case
  instance?: string;      // Đường dẫn API gây ra lỗi
  errors?: Record<string, string[]>; // Chi tiết lỗi validation theo từng trường
  traceId: string;        // Mã trace để tra cứu log
  timestamp: string;      // Thời điểm xảy ra lỗi
}
```

**Ví dụ lỗi Validation (HTTP 400):**
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "errorCode": "VAL_INVALID_INPUT",
  "detail": "One or more validation errors occurred.",
  "errors": {
    "email": ["Email is not a valid email address."],
    "password": ["Password must be at least 8 characters long."]
  },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "timestamp": "2026-10-27T08:00:00Z"
}
```

### Hướng dẫn xử lý lỗi cho Frontend:
1. **HTTP 401 Unauthorized (`ERR_INVALID_TOKEN`, `UNAUTHORIZED`)**: Access Token hết hạn hoặc không hợp lệ -> Client kích hoạt luồng gọi ngầm `POST /api/v1/auth/refresh` bằng Refresh Token. Nếu thất bại, chuyển hướng về trang Đăng nhập.
2. **HTTP 403 Forbidden (`FORBIDDEN`)**: Người dùng không đủ quyền thực hiện hành động -> Hiển thị thông báo hoặc ẩn nút chức năng.
3. **HTTP 404 Not Found (`USER_NOT_FOUND`, `PRODUCT_NOT_FOUND`)**: Không tìm thấy tài nguyên -> Chuyển về trang 404 hoặc thông báo sản phẩm/đơn hàng không tồn tại.
4. **HTTP 409 Conflict (`EMAIL_ALREADY_EXISTS`, `OUT_OF_STOCK`)**: Xung đột dữ liệu -> Báo lỗi cụ thể trên Form để người dùng sửa đổi.
5. **HTTP 429 Too Many Requests (`ERR_MAX_OTP_ATTEMPTS`, `RATE_LIMIT_EXCEEDED`)**: Vượt quá tần suất gọi API -> Khóa nút bấm và kích hoạt bộ đếm thời gian chờ (Cooldown Timer).

---

## 5. HỆ THỐNG PHÂN QUYỀN (RBAC MATRIX)

Hệ thống hỗ trợ 6 vai trò chính:
- `Guest`: Người dùng vãng lai chưa đăng nhập (Xem sản phẩm, danh mục, đấu giá, đăng ký tài khoản).
- `Buyer`: Người mua hàng đã xác thực tài khoản (Quản lý giỏ hàng, đặt hàng, thanh toán ví, tham gia đấu giá, theo dõi vận đơn).
- `Seller`: Người bán hàng đã được Admin phê duyệt (Đăng bán sản phẩm, quản lý kho SKU, tạo phiên đấu giá, duyệt đơn con, tạo vận đơn).
- `Staff`: Nhân viên vận hành hệ thống (Hỗ trợ khách hàng, kiểm duyệt đơn khiếu nại).
- `Admin`: Quản trị viên (Phê duyệt người bán, duyệt sản phẩm/phiên đấu giá, khóa tài khoản, xem thống kê).
- `SuperAdmin`: Quản trị viên cấp cao nhất (Toàn quyền hệ thống, phân quyền vai trò, cấu hình hệ thống).

---

## 6. DANH MỤC 11 MODULE API (ECOSYSTEM SPECIFICATIONS)

Các tài liệu bên dưới được chia theo trạng thái trong repository này. `B_NONE` nghĩa là API chưa được triển khai tại đây hoặc chưa có source để xác minh; không khẳng định trạng thái của service trong repository khác.

| STT | File Tài Liệu | Dịch Vụ Backend | Số API | Mô Tả Nghiệp Vụ Chính |
| :---: | :--- | :--- | :---: | :--- |
| **01** | [**01_AUTH_API.md**](./B_NONE/01_AUTH_API.md) | `UserService (8001)` | **10** | Đăng ký, Đăng nhập, Xác thực OTP, Refresh Token (RTR), Đổi/Quên mật khẩu, Quản lý phiên. |
| **02** | [**02_USER_PROFILE_API.md**](./B_NONE/02_USER_PROFILE_API.md) | `UserService (8001)` | **24** | Thông tin cá nhân, Sổ địa chỉ, Xác thực danh tính KYC, Tài khoản ngân hàng, Nộp đơn Người bán. |
| **03** | [**03_ADMIN_USERS_API.md**](./B_NONE/03_ADMIN_USERS_API.md) | `UserService (8001)` | **26** | Phê duyệt Seller, Khóa/Mở tài khoản, Xử lý báo cáo vi phạm, Phân quyền vai trò hệ thống. |
| **04** | [**04_PRODUCTS_CATALOG_API.md**](./B_NONE/04_PRODUCTS_CATALOG_API.md) | `CatalogService (8002)` | **30** | Tìm kiếm sản phẩm, Chi tiết sản phẩm, CRUD sản phẩm Seller, Biến thể SKU, Cây danh mục, Tồn kho. |
| **05** | [**05_CART_COMMERCE_API.md**](./B_NONE/05_CART_COMMERCE_API.md) | `CommerceService (8003)` | **6** | Xem giỏ hàng, Thêm sản phẩm, Cập nhật số lượng, Xóa món, Chọn sản phẩm thanh toán. |
| **06** | [**06_ORDERS_CHECKOUT_API.md**](./B_NONE/06_ORDERS_CHECKOUT_API.md) | `CommerceService (8003)` | **13** | Xem trước thanh toán, Đặt hàng, Đơn con theo Shop, Tiếp nhận đơn, Đánh giá, Hoàn tiền, Webhook. |
| **07** | [**07_AUCTIONS_API.md**](./B_NONE/07_AUCTIONS_API.md) | `AuctionService (8005)` | **17** | Danh sách đấu giá, Đặt cọc, Đặt giá đấu (Bid), Mua ngay (Buy Now), Lịch sử đấu giá, Quản lý phiên. |
| **08** | [**08_FULFILLMENT_SHIPPING_API.md**](./B_NONE/08_FULFILLMENT_SHIPPING_API.md) | `FulfillmentService (8006)` | **9** | Tính cước vận chuyển (GHN/GHTK/ViettelPost), Tra cứu vận đơn (Tracking timeline), Tạo & hủy vận đơn. |
| **09** | [**WalletService specification**](./B_NONE/09_WALLET_PAYMENTS_API.md) / [**Implemented WalletService routes**](./A_ACTIVE/09_WALLET_PAYMENTS_API.md) | `WalletService (8008)` | **8** | Đặc tả ecosystem và các route đã xác minh trong service; payout ngân hàng chưa hoàn chỉnh. |
| **10** | [**10_NOTIFICATIONS_API.md**](./B_NONE/10_NOTIFICATIONS_API.md) | `NotificationService (8007)` | **14** | Thông báo trong app, Đánh dấu đã đọc, Đăng ký FCM Device Token, Cấu hình kênh nhận tin. |
| **11** | [**11_GATEWAY_CONTROL_AND_HEALTH_API.md**](./B_NONE/11_GATEWAY_CONTROL_AND_HEALTH_API.md) | `ApiGateway (8080)` | **17** | Điều khiển bật/tắt Mock Mode, Auth Bypass, Reload dữ liệu Seeds/, Health check microservices & DB. |
| **TỔNG** | **11 File Tài Liệu** | **Toàn Bộ Hệ Thống** | **174** | **Toàn bộ bề mặt API của hệ thống Backend Nexus.** |

---

## 7. LIÊN HỆ & HỖ TRỢ KỸ THUẬT

- **API Gateway Swagger UI**: [http://localhost:8080/swagger](http://localhost:8080/swagger)
- **Kiểm tra trạng thái hệ thống**: `GET http://localhost:8080/api/v1/gateway/prober/status`
- **Tài liệu đặc tả kiến trúc nội bộ**: Xem thêm tại thư mục `specs/` ở root của dự án.
