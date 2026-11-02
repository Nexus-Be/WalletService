# 08 — TÀI LIỆU API VẬN CHUYỂN & GIAO NHẬN (FULFILLMENT & SHIPPING API)

> **Dịch vụ phụ trách:** `FulfillmentService (Port 8006 / Gateway Proxy: Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `9 endpoints`  

Tài liệu chuẩn hóa toàn bộ các API tích hợp vận chuyển: Tính toán cước phí vận chuyển đa đơn vị (GHN, GHTK, ViettelPost), Tra cứu tiến trình vận đơn (Tracking timeline), Tạo vận đơn giao hàng, Hủy vận đơn và Webhook đồng bộ trạng thái hãng vận chuyển.

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `POST` | [`/api/v1/checkout/quote`](#1-post-api-v1-checkout-quote) | `Authenticated User` (Yêu cầu JWT Bearer Token) | buyer nhập địa chi, mã giảm giá và coi trước thông tin sau giảm và thông tin tiền ship tính trước |
| 2 | `GET` | [`/api/v1/shipments/{shipmentId}`](#2-get-api-v1-shipments-shipmentid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | coi thông tin chi tiết của 1 shipment |
| 3 | `GET` | [`/api/v1/shipments/order/{orderId}`](#3-get-api-v1-shipments-order-orderid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | coi order đó có mấy shipment |
| 4 | `GET` | [`/api/v1/seller/shipment`](#4-get-api-v1-seller-shipment) | `Seller` | lấy danh sách các đơn ship của selller phải giao |
| 5 | `GET` | [`/api/v1/seller/shipment/search`](#5-get-api-v1-seller-shipment-search) | `Seller` | filter, search shipment của 1 seller, lọc trạng thái |
| 6 | `GET` | [`/api/v1/seller/shipment/{shipmentId}`](#6-get-api-v1-seller-shipment-shipmentid-) | `Seller` | coi chi tiết cái shipment của seller đó |
| 7 | `PATCH` | [`/api/v1/shipments/{shipmentId}/tracking`](#7-patch-api-v1-shipments-shipmentid-tracking) | `Authenticated User` (Yêu cầu JWT Bearer Token) | cập nhập trạng thái cái shipment từ ghn |
| 8 | `POST` | [`/api/v1/shipment/{shipmentId}/complete`](#8-post-api-v1-shipment-shipmentid-complete) | `Authenticated User` (Yêu cầu JWT Bearer Token) | người dùng ấn nút đã nhận được hàng |
| 9 | `POST` | [`/api/v1/shipments/webhook`](#9-post-api-v1-shipments-webhook) | `Authenticated User` (Yêu cầu JWT Bearer Token) | nhận thông tin từ ghn.  nếu nhận được đơn hàng hoàn thành, thì đổi trạng thái những cái liên quan |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-post-api-v1-checkout-quote"></a>
### 1. `POST /api/v1/checkout/quote`

**buyer nhập địa chi, mã giảm giá và coi trước thông tin sau giảm và thông tin tiền ship tính trước**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/checkout/quote`
- **Mã tham chiếu Excel**: `CHK-01 (khung) + FUL-12 + CART-07 + CAT-24`

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
  "cartItemIds": ["uuid"],
  "shippingAddress": {
  "receiverName": "string",
  "street": "string",
  "ward": "string",
  "district": "string",
  "province": "string",
  "ghnDistrictId": "int",
  "ghnWardCode": "string"
  },
  "contactPhone": "string",
  "paymentMethod": "WALLET|VNPAY",
  "carrierCode": "string",
  "vouchers": {
  "system": "string",
  "shops": [{"sellerId": "uuid", "code": "string"}],
  "products": [{"skuId": "uuid", "code": "string"}]
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
    "data": {
      "orders": [
        {
          "sellerId": "uuid",
          "items": "array",
          "itemsAmount": "decimal",
          "productDiscountAmount": "decimal",
          "shopDiscountAmount": "decimal",
          "platformMarkupAmount": "decimal",
          "systemDiscountAmount": "decimal",
          "shippingFeeAmount": "decimal",
          "totalAmount": "decimal",
          "shippingOptions": [
            {
              "fee": "decimal",
              "serviceType": "Standard|Express|SameDay",
              "estimatedDeliveryAt": "datetime"
            }
          ],
          "quoteId": "string"
        }
      ],
      "grandTotal": "decimal",
      "appliedVouchers": "array",
      "invalidVouchers": [
        {
          "code": "string",
          "reason": "string"
        }
      ],
      "walletBalance": "decimal",
      "warnings": [
        "SHIPPING_QUOTE_UNAVAILABLE"
      ]
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_ADDRESS` | Shipping address is incomplete. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `CART_EMPTY` | No valid items selected for checkout. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `VOUCHER_INVALID` | One or more vouchers are invalid or expired. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="2-get-api-v1-shipments-shipmentid-"></a>
### 2. `GET /api/v1/shipments/{shipmentId}`

**coi thông tin chi tiết của 1 shipment**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/shipments/{shipmentId}`
- **Mã tham chiếu Excel**: `FUL-06`

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
    "shipmentId": "uuid"
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
      "shipmentId": "uuid",
      "orderId": "uuid",
      "subOrderId": "uuid",
      "carrierName": "GHN",
      "trackingNumber": "GHN1029384756",
      "status": "IN_TRANSIT",
      "shippingFee": 30000,
      "codAmount": 0,
      "receiverName": "Nguyen Van A",
      "receiverPhone": "0901234567",
      "receiverAddress": "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
      "events": [
        {
          "status": "PICKED_UP",
          "location": "Kho Tan Binh",
          "description": "Shipper picked up parcel from seller.",
          "eventAt": "2026-10-26T08:00:00Z"
        },
        {
          "status": "IN_TRANSIT",
          "location": "Kho HN SOC",
          "description": "Parcel is in transit to distribution hub.",
          "eventAt": "2026-10-26T12:00:00Z"
        }
      ],
      "estimatedDeliveryAt": "2026-10-28T18:00:00Z",
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
| `404` | `SHIPMENT_NOT_FOUND` | Shipment not found with the provided identifier. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="3-get-api-v1-shipments-order-orderid-"></a>
### 3. `GET /api/v1/shipments/order/{orderId}`

**coi order đó có mấy shipment**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/shipments/order/{orderId}`
- **Mã tham chiếu Excel**: `FUL-07`

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
      "shipments": [
        {
          "shipmentId": "uuid",
          "subOrderId": "uuid",
          "carrierName": "GHN",
          "trackingNumber": "GHN1029384756",
          "status": "IN_TRANSIT",
          "shippingFee": 30000,
          "estimatedDeliveryAt": "2026-10-28T18:00:00Z"
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
| `404` | `ORDER_NOT_FOUND` | Order was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="4-get-api-v1-seller-shipment"></a>
### 4. `GET /api/v1/seller/shipment`

**lấy danh sách các đơn ship của selller phải giao**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/seller/shipment`
- **Mã tham chiếu Excel**: `FUL-08`

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
    "size": 20,
    "status": "READY_TO_SHIP|IN_TRANSIT|DELIVERED"
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
          "shipmentId": "uuid",
          "subOrderId": "uuid",
          "trackingNumber": "GHN1029384756",
          "carrierName": "GHN",
          "status": "READY_TO_SHIP",
          "shippingFee": 30000,
          "receiverName": "Nguyen Van A",
          "receiverPhone": "0901234567",
          "createdAt": "2026-10-26T00:00:00Z"
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
| `403` | `FORBIDDEN` | Only sellers can access shipment management. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="5-get-api-v1-seller-shipment-search"></a>
### 5. `GET /api/v1/seller/shipment/search`

**filter, search shipment của 1 seller, lọc trạng thái**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/seller/shipment/search`
- **Mã tham chiếu Excel**: `FUL-09`

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
    "keyword": "GHN1029384756",
    "status": "IN_TRANSIT",
    "fromDate": "2026-10-01",
    "toDate": "2026-10-26",
    "page": 1,
    "size": 20
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
          "shipmentId": "uuid",
          "trackingNumber": "GHN1029384756",
          "subOrderId": "uuid",
          "carrierName": "GHN",
          "status": "IN_TRANSIT",
          "receiverName": "Nguyen Van A",
          "createdAt": "2026-10-26T00:00:00Z"
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
| `400` | `VAL_INVALID_INPUT` | Query parameters are invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only sellers can access shipment management. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="6-get-api-v1-seller-shipment-shipmentid-"></a>
### 6. `GET /api/v1/seller/shipment/{shipmentId}`

**coi chi tiết cái shipment của seller đó**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/seller/shipment/{shipmentId}`
- **Mã tham chiếu Excel**: `FUL-10`

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
    "shipmentId": "uuid"
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
          "skuId": "uuid",
          "productName": "Chuot gaming khong day",
          "quantity": 1
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
| `403` | `FORBIDDEN` | You do not have permission to view this shipment. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `SHIPMENT_NOT_FOUND` | Shipment not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="7-patch-api-v1-shipments-shipmentid-tracking"></a>
### 7. `PATCH /api/v1/shipments/{shipmentId}/tracking`

**cập nhập trạng thái cái shipment từ ghn**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/shipments/{shipmentId}/tracking`
- **Mã tham chiếu Excel**: `FUL-11`

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
    "shipmentId": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "status": "IN_TRANSIT|OUT_FOR_DELIVERY|DELIVERED",
    "location": "Buu cuc Quan 1",
    "description": "Shipper is delivering parcel to customer.",
    "timestamp": "2026-10-26T14:00:00Z"
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
      "shipmentId": "uuid",
      "trackingNumber": "GHN1029384756",
      "currentStatus": "OUT_FOR_DELIVERY",
      "updatedAt": "2026-10-26T14:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `INVALID_SHIPMENT_STATUS` | Shipment status transition is not permitted. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `SHIPMENT_NOT_FOUND` | Shipment not found with the provided identifier. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="8-post-api-v1-shipment-shipmentid-complete"></a>
### 8. `POST /api/v1/shipment/{shipmentId}/complete`

**người dùng ấn nút đã nhận được hàng**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/shipment/{shipmentId}/complete`
- **Mã tham chiếu Excel**: `FUL-13`

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
    "shipmentId": "uuid"
  }
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
    "data": {
      "shipmentId": "uuid",
      "status": "COMPLETED",
      "completedAt": "2026-10-26T00:00:00Z"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `INVALID_SHIPMENT_STATUS` | Shipment can only be completed when delivered. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `SHIPMENT_NOT_FOUND` | Shipment not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="9-post-api-v1-shipments-webhook"></a>
### 9. `POST /api/v1/shipments/webhook`

**nhận thông tin từ ghn. 
nếu nhận được đơn hàng hoàn thành, thì đổi trạng thái những cái liên quan**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/shipments/webhook`
- **Mã tham chiếu Excel**: `FUL-14`

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
    "trackingNumber": "GHN1029384756",
    "status": "DELIVERED",
    "location": "Quan 1, TP.HCM",
    "note": "Delivered to recipient.",
    "timestamp": "2026-10-26T16:00:00Z"
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
    "message": "Webhook event processed.",
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
| `400` | `CARRIER_WEBHOOK_INVALID_SIGNATURE` | Invalid webhook signature from shipping provider. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `SHIPMENT_NOT_FOUND` | Tracking number does not match any existing shipment. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---
