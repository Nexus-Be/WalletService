# 05 — TÀI LIỆU API GIỎ HÀNG THƯƠNG MẠI (SHOPPING CART API)

> **Dịch vụ phụ trách:** `CommerceService (Port 8003 / Gateway Proxy: Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `6 endpoints`  

Tài liệu chuẩn hóa toàn bộ các API thao tác với giỏ hàng của Người mua (Buyer): xem giỏ hàng, thêm sản phẩm/biến thể, cập nhật số lượng, xóa sản phẩm, xóa toàn bộ và chọn lọc sản phẩm trước khi thanh toán.

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `POST` | [`/api/v1/cart/items`](#1-post-api-v1-cart-items) | `Buyer` (Yêu cầu JWT Bearer Token) | thêm 1 hoặc nhiều sản phẩm vào giỏ hàng |
| 2 | `GET` | [`/api/v1/cart`](#2-get-api-v1-cart) | `Buyer` (Yêu cầu JWT Bearer Token) | xem toàn bộ các sản phẩm của giỏ hàng, gửi list cart lên và kho check cái list đó và catalogc |
| 3 | `PATCH` | [`/api/v1/cart/items/{id}`](#3-patch-api-v1-cart-items-id-) | `Buyer` (Yêu cầu JWT Bearer Token) | đổi số lượng 1 sản phẩm trong giỏ hàng |
| 4 | `DELETE` | [`/api/v1/cart/items`](#4-delete-api-v1-cart-items) | `Buyer` (Yêu cầu JWT Bearer Token) | xóa 1 hoặc nhiều sản phẩm ra khỏi giỏ |
| 5 | `DELETE` | [`/api/v1/cart/items/all`](#5-delete-api-v1-cart-items-all) | `Buyer` (Yêu cầu JWT Bearer Token) | xóa toàn bộ sản phẩm ra khỏi giỏ |
| 6 | `GET` | [`/api/v1/cart/items/select`](#6-get-api-v1-cart-items-select) | `Buyer` (Yêu cầu JWT Bearer Token) | buyer select ccasc item bên trong cart và trả ra giá tiền |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-post-api-v1-cart-items"></a>
### 1. `POST /api/v1/cart/items`

**thêm 1 hoặc nhiều sản phẩm vào giỏ hàng**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/cart/items`
- **Mã tham chiếu Excel**: `CART-02`

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
  {"productId": "uuid", "skuId": "uuid", "quantity": "int"}
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
      "cartItemId": "uuid",
      "quantity": "int",
      "unitPriceSnapshot": "decimal",
      "cart": {
        "itemCount": "int",
        "subtotalAmount": "decimal"
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_QUANTITY` | Quantity must be greater than zero. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `STOCK_INSUFFICIENT` | Requested quantity exceeds available stock. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `PRODUCT_NOT_FOUND` | Product or SKU does not exist. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="2-get-api-v1-cart"></a>
### 2. `GET /api/v1/cart`

**xem toàn bộ các sản phẩm của giỏ hàng, gửi list cart lên và kho check cái list đó và catalogc**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/cart`
- **Mã tham chiếu Excel**: `CART-01 (ô ID tham chiếu ghi NULL) + FUL-04 + CAT-39 + CAT-24`

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
  {"includeInvalid": "boolean"}
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
      "cartId": "uuid",
      "status": "Active|Abandoned|Converted",
      "itemCount": "int",
      "subtotalAmount": "decimal",
      "lastActivityAt": "datetime",
      "groups": [
        {
          "sellerId": "uuid",
          "sellerName": "string",
          "items": [
            {
              "cartItemId": "uuid",
              "productId": "uuid",
              "skuId": "uuid",
              "productName": "string",
              "skuAttributes": "object",
              "imageUrl": "string",
              "quantity": "int",
              "unitPriceSnapshot": "decimal",
              "currentUnitPrice": "decimal",
              "lineTotal": "decimal",
              "priceChanged": "boolean",
              "stockAvailable": "int",
              "itemStatus": "OK|OUT_OF_STOCK|INACTIVE|PRICE_CHANGED"
            }
          ]
        }
      ],
      "warnings": [
        "CATALOG_UNAVAILABLE|FULFILLMENT_UNAVAILABLE"
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
| `404` | `CART_NOT_FOUND` | User cart was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="3-patch-api-v1-cart-items-id-"></a>
### 3. `PATCH /api/v1/cart/items/{id}`

**đổi số lượng 1 sản phẩm trong giỏ hàng**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/cart/items/{id}`
- **Mã tham chiếu Excel**: `CART-03`

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
  {"id": "uuid"}
  ```

- **Request Body (JSON Payload):**
  ```json
  {"quantity": "int"}
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
      "cartItemId": "uuid",
      "quantity": "int",
      "lineTotal": "decimal",
      "cart": {
        "itemCount": "int",
        "subtotalAmount": "decimal"
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_QUANTITY` | Quantity must be greater than zero. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `STOCK_INSUFFICIENT` | Requested quantity exceeds available stock. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `CART_ITEM_NOT_FOUND` | Cart item does not exist in your cart. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="4-delete-api-v1-cart-items"></a>
### 4. `DELETE /api/v1/cart/items`

**xóa 1 hoặc nhiều sản phẩm ra khỏi giỏ**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `DELETE`
- **Đường dẫn**: `/api/v1/cart/items`
- **Mã tham chiếu Excel**: `CART-04`

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
      "cart": {
        "itemCount": "int",
        "subtotalAmount": "decimal"
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_INPUT` | Cart item IDs must be provided. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `404` | `CART_ITEM_NOT_FOUND` | One or more cart items were not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="5-delete-api-v1-cart-items-all"></a>
### 5. `DELETE /api/v1/cart/items/all`

**xóa toàn bộ sản phẩm ra khỏi giỏ**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `DELETE`
- **Đường dẫn**: `/api/v1/cart/items/all`
- **Mã tham chiếu Excel**: `CART-05`

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
  {"all": "boolean"}
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
      "removedCount": "int",
      "cart": {
        "itemCount": "int",
        "subtotalAmount": "decimal"
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
| `404` | `CART_NOT_FOUND` | User cart was not found. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="6-get-api-v1-cart-items-select"></a>
### 6. `GET /api/v1/cart/items/select`

**buyer select ccasc item bên trong cart và trả ra giá tiền**

- **Quyền hạn (RBAC)**: `Buyer` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/cart/items/select`
- **Mã tham chiếu Excel**: `CART-06`

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
  "cartItemIds": ["uuid"]
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
          "cartItemId": "uuid",
          "issues": [
            "PRICE_CHANGED|OUT_OF_STOCK|INACTIVE|SELLER_SUSPENDED|QTY_EXCEEDS_STOCK"
          ],
          "currentUnitPrice": "decimal",
          "availableQty": "int"
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
| `400` | `VAL_INVALID_INPUT` | Cart item IDs are required. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---
