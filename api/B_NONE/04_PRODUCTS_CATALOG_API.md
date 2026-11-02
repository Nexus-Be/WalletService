# 04 — TÀI LIỆU API SẢN PHẨM, DANH MỤC & KHO HÀNG (CATALOG & PRODUCTS API)

> **Dịch vụ phụ trách:** `CatalogService (Port 8002 / Gateway Proxy: Port 8080)`  
> **Cổng Gateway duy nhất cho Frontend:** `http://localhost:8080`  
> **Số lượng Endpoint:** `30 endpoints`  

Tài liệu chuẩn hóa toàn bộ các API xem sản phẩm công khai, tìm kiếm nâng cao đa tiêu chí, quản lý sản phẩm người bán (Seller Product CRUD, biến thể SKU, hình ảnh), danh mục hàng hóa đa cấp và quản lý tồn kho.

---

## 📌 BẢNG TRA CỨU NHANH (QUICK REFERENCE TABLE)

| STT | Phương thức | Đường dẫn Endpoint | Phân quyền (RBAC) | Mô tả tóm tắt |
| :---: | :---: | :--- | :--- | :--- |
| 1 | `GET` | [`/api/v1/products`](#1-get-api-v1-products) | `Public` / Mọi người dùng | lấy toàn bộ product |
| 2 | `GET` | [`/api/v1/products/search`](#2-get-api-v1-products-search) | `Public` / Mọi người dùng | filter, search product |
| 3 | `GET` | [`/api/v1/product/{productId}`](#3-get-api-v1-product-productid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | lấy thông tin chi tiết của 1 product |
| 4 | `POST` | [`/api/v1/order/{orderId}/products/{productId}/reviews`](#4-post-api-v1-order-orderid-products-productid-reviews) | `Authenticated User` (Yêu cầu JWT Bearer Token) | ddbuyer đánh giá 1 sản phẩm, check sản phẩm đã được mua chưa, đã hoàn thành chưa, có phải người này ko. phải điền order id vào |
| 5 | `PATCH` | [`/api/v1/order/{orderId}/products/{productId}/reviews`](#5-patch-api-v1-order-orderid-products-productid-reviews) | `Authenticated User` (Yêu cầu JWT Bearer Token) | chỉnh sửa đánh giá sản phẩm |
| 6 | `GET` | [`/api/v1/products`](#6-get-api-v1-products) | `Authenticated User` (Yêu cầu JWT Bearer Token) | xem toàn bộ các sản phẩm của shop mình |
| 7 | `GET` | [`/api/v1/product/search`](#7-get-api-v1-product-search) | `Authenticated User` (Yêu cầu JWT Bearer Token) | search, filter các sản phẩm của shop mình |
| 8 | `GET` | [`/api/v1/product/{productId}`](#8-get-api-v1-product-productid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | coi thông tin detail của 1 sản phẩm |
| 9 | `POST` | [`/api/v1/products`](#9-post-api-v1-products) | `Authenticated User` (Yêu cầu JWT Bearer Token) | tạo sản phẩm mới, kèm biến thể đầu, KÈM DANH MỤC |
| 10 | `PATCH` | [`/api/v1/product/{productId}`](#10-patch-api-v1-product-productid-) | `Seller` | seller cập nhật thông tin sản phẩm, danh mục |
| 11 | `DELETE` | [`/api/v1/product/{productId}`](#11-delete-api-v1-product-productid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Xóa sản phẩm |
| 12 | `POST` | [`/api/v1/product/{productId}pics`](#12-post-api-v1-product-productid-pics) | `Authenticated User` (Yêu cầu JWT Bearer Token) | thêm list ảnh của sản phẩm |
| 13 | `GET` | [`/api/v1/product/{productId}pics`](#13-get-api-v1-product-productid-pics) | `Authenticated User` (Yêu cầu JWT Bearer Token) | xem các ảnh của sản phẩm |
| 14 | `DELETE` | [`/api/v1/product/{productId}pics`](#14-delete-api-v1-product-productid-pics) | `Authenticated User` (Yêu cầu JWT Bearer Token) | xóa list ảnh sản phẩm |
| 15 | `GET` | [`/api/v1/products/{productId}/totalRating`](#15-get-api-v1-products-productid-totalrating) | `Authenticated User` (Yêu cầu JWT Bearer Token) | xem số sao của 1 sản phẩm, trung bình |
| 16 | `GET` | [`/api/v1/products/{productId}/rating`](#16-get-api-v1-products-productid-rating) | `Authenticated User` (Yêu cầu JWT Bearer Token) | tổng số sao, và đánh giá của các người mua trong product |
| 17 | `PATCH` | [`/api/v1/product/{productId}/status`](#17-patch-api-v1-product-productid-status) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Cập nhật trạng thái sản phẩm |
| 18 | `POST` | [`/api/v1/inventory/stock-in`](#18-post-api-v1-inventory-stock-in) | `Authenticated User` (Yêu cầu JWT Bearer Token) | thêm hàng vào kho, 1 list, kèm số lượng thêm, kèm chủng loại muốn thêm |
| 19 | `POST` | [`/api/v1/product/{productId}/variants`](#19-post-api-v1-product-productid-variants) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Thêm biến thể cho sản phẩm |
| 20 | `DELETE` | [`/api/v1/variant/{variantId}`](#20-delete-api-v1-variant-variantid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | xóa 1 biến thể |
| 21 | `PATCH` | [`/api/v1/variant/{variantId}`](#21-patch-api-v1-variant-variantid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Cập nhật thông tin của sản phẩm biến thể |
| 22 | `GET` | [`/api/v1/variant/{variantId}`](#22-get-api-v1-variant-variantid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | xem thông tin chi tiết của 1 biến thể |
| 23 | `POST` | [`/api/v1/variant/{variantId}/stock-in`](#23-post-api-v1-variant-variantid-stock-in) | `Authenticated User` (Yêu cầu JWT Bearer Token) | thêm biến thể của sản phảm vào kho |
| 24 | `GET` | [`/api/v1/categories`](#24-get-api-v1-categories) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Xem danh sách danh mục |
| 25 | `POST` | [`/api/v1/categories`](#25-post-api-v1-categories) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Tạo danh mục mới, có thể truyền danh mục cha, nếu ko truyền nó là cha |
| 26 | `PUT` | [`/api/v1/categories/{categoryId}`](#26-put-api-v1-categories-categoryid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Cập nhật danh mục |
| 27 | `DELETE` | [`/api/v1/categories`](#27-delete-api-v1-categories) | `Authenticated User` (Yêu cầu JWT Bearer Token) | Xóa mềm danh mục |
| 28 | `GET` | [`/api/v1/categories/{categoryId}`](#28-get-api-v1-categories-categoryid-) | `Authenticated User` (Yêu cầu JWT Bearer Token) | xem danh sách các danh mục con và thông tin 1 danh mục cha |
| 29 | `POST` | [`/api/v1/product/auction`](#29-post-api-v1-product-auction) | `Authenticated User` (Yêu cầu JWT Bearer Token) | thêm sản phẩm đấu giá |
| 30 | `GET` | [`/api/v1/product/aucction/search`](#30-get-api-v1-product-aucction-search) | `Seller` | coi đươc các sản phẩm seller đó đã đấu giá, filter |

---

## 📖 CHI TIẾT ĐẶC TẢ TỪNG ENDPOINT

<a id="1-get-api-v1-products"></a>
### 1. `GET /api/v1/products`

**lấy toàn bộ product**

- **Quyền hạn (RBAC)**: `Public` / Mọi người dùng
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/products`
- **Mã tham chiếu Excel**: `CAT-06`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Query Parameters:**
  ```json
  {
  "page": "int",
  "pageSize": "int",
  "keyword": "string",
  "categoryId": "uuid",
  "status": "DRAFT|ACTIVE|INACTIVE",
  "sellerId": "uuid",
  "sortBy": "string",
  "sortDir": "string"
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
          "minPrice": "decimal",
          "maxPrice": "decimal",
          "primaryImageUrl": "string",
          "avgRating": "decimal",
          "status": "DRAFT|ACTIVE|INACTIVE"
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
| `400` | `VAL_INVALID_PAGE` | Page and pageSize must be positive integers. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="2-get-api-v1-products-search"></a>
### 2. `GET /api/v1/products/search`

**filter, search product**

- **Quyền hạn (RBAC)**: `Public` / Mọi người dùng
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/products/search`
- **Mã tham chiếu Excel**: `CAT-20 + CAT-21`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Query Parameters:**
  ```json
  {
  "keyword": "string",
  "categoryId": "uuid",
  "minPrice": "decimal",
  "maxPrice": "decimal",
  "page": "int",
  "pageSize": "int",
  "brand": "string",
  "saleType": "REGULAR|AUCTION",
  "attributes": "string (AttrName=AttrValue)",
  "minRating": "decimal",
  "sortBy": "newest|priceAsc|priceDesc|rating"
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
          "minPrice": "decimal",
          "maxPrice": "decimal",
          "primaryImageUrl": "string",
          "avgRating": "decimal"
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
| `400` | `VAL_INVALID_FILTER` | Search filter parameters are invalid. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="3-get-api-v1-product-productid-"></a>
### 3. `GET /api/v1/product/{productId}`

**lấy thông tin chi tiết của 1 product**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/product/{productId}`
- **Mã tham chiếu Excel**: `CAT-05`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  {"productId": "uuid"}
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
      "product": {
        "productId": "uuid",
        "sellerId": "uuid",
        "categoryId": "uuid",
        "name": "string",
        "description": "string",
        "brand": "string",
        "saleType": "REGULAR|AUCTION",
        "status": "DRAFT|ACTIVE|INACTIVE",
        "statusReason": "SELLER_PAUSED|DISCONTINUED|ADMIN_LOCKED|OTHER",
        "specifications": "object",
        "publishedAt": "datetime"
      },
      "category": {
        "categoryId": "uuid",
        "parentId": "uuid",
        "name": "string",
        "description": "string",
        "level": "int",
        "sortOrder": "int",
        "isActive": "boolean"
      },
      "variants": [
        {
          "variantId": "uuid",
          "skuCode": "string",
          "name": "string",
          "price": "decimal",
          "status": "ACTIVE|INACTIVE",
          "attributes": [
            {
              "attrName": "string",
              "attrValue": "string"
            }
          ]
        }
      ],
      "images": [
        {
          "url": "string",
          "altText": "string"
        }
      ],
      "rating": {
        "avgRating": "decimal",
        "reviewCount": "int"
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VAL_INVALID_PRODUCT_ID` | Product ID format is invalid. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `PRODUCT_NOT_FOUND` | Product was not found or is inactive. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="4-post-api-v1-order-orderid-products-productid-reviews"></a>
### 4. `POST /api/v1/order/{orderId}/products/{productId}/reviews`

**ddbuyer đánh giá 1 sản phẩm, check sản phẩm đã được mua chưa, đã hoàn thành chưa, có phải người này ko. phải điền order id vào**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/order/{orderId}/products/{productId}/reviews`
- **Mã tham chiếu Excel**: `ORD-07`

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
    "productId": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "rating": 5,
    "comment": "San pham rat tot, dong goi ky.",
    "images": ["https://img.example.com/review1.png"]
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
      "reviewId": "uuid",
      "orderId": "uuid",
      "productId": "uuid",
      "rating": 5,
      "comment": "San pham rat tot, dong goi ky.",
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
| `400` | `ORDER_NOT_COMPLETED` | Reviews can only be submitted for completed orders. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `409` | `REVIEW_ALREADY_EXISTS` | You have already reviewed this product. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="5-patch-api-v1-order-orderid-products-productid-reviews"></a>
### 5. `PATCH /api/v1/order/{orderId}/products/{productId}/reviews`

**chỉnh sửa đánh giá sản phẩm**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/order/{orderId}/products/{productId}/reviews`
- **Mã tham chiếu Excel**: `ORD-07`

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
    "productId": "uuid"
  }
  ```

- **Request Body (JSON Payload):**
  ```json
  {
    "rating": 4,
    "comment": "Cap nhat: San pham dung tot sau 1 tuan.",
    "images": ["https://img.example.com/review2.png"]
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
      "reviewId": "uuid",
      "orderId": "uuid",
      "productId": "uuid",
      "rating": 4,
      "comment": "Cap nhat: San pham dung tot sau 1 tuan.",
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
| `403` | `FORBIDDEN` | You can only edit your own reviews. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `REVIEW_NOT_FOUND` | Review does not exist. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="6-get-api-v1-products"></a>
### 6. `GET /api/v1/products`

**xem toàn bộ các sản phẩm của shop mình**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/products`
- **Mã tham chiếu Excel**: `CAT-06`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT> (optional)"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Query Parameters:**
  ```json
  {
  "page": "int",
  "pageSize": "int (tối đa 100)",
  "keyword": "string",
  "categoryId": "uuid",
  "status": "DRAFT|ACTIVE|INACTIVE",
  "sellerId": "uuid",
  "saleType": "REGULAR|AUCTION",
  "sortBy": "newest|priceAsc|priceDesc|rating",
  "sortDir": "asc|desc"
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
| `400` | `VALIDATION_ERROR` | Page < 1, pageSize outside 1..100, invalid sortBy or sortDir, or invalid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Provided token is invalid or has expired. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |

---

<a id="7-get-api-v1-product-search"></a>
### 7. `GET /api/v1/product/search`

**search, filter các sản phẩm của shop mình**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/product/search`
- **Mã tham chiếu Excel**: `CAT-06 + CAT-21`

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
  "keyword": "string",
  "categoryId": "uuid",
  "minPrice": "decimal",
  "maxPrice": "decimal",
  "brand": "string",
  "saleType": "REGULAR|AUCTION",
  "attributes": "string (AttrName=AttrValue)",
  "minRating": "decimal (0..5)",
  "status": "DRAFT|ACTIVE|INACTIVE",
  "sortBy": "newest|priceAsc|priceDesc|rating",
  "page": "int",
  "pageSize": "int"
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
| `400` | `VALIDATION_ERROR` | Minprice > maxprice, rating outside 0..5, invalid sortBy, invalid attribute format, or invalid pagination. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only sellers or administrators can perform this action. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---

<a id="8-get-api-v1-product-productid-"></a>
### 8. `GET /api/v1/product/{productId}`

**coi thông tin detail của 1 sản phẩm**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/product/{productId}`
- **Mã tham chiếu Excel**: `CAT-05`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT> (optional)"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  {"productId": "uuid"}
  ```

- **Query Parameters:**
  ```json
  {"auctionId": "uuid (tùy chọn, dùng khi xem sản phẩm từ trang phiên đấu giá)"}
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
      "product": {
        "productId": "uuid",
        "sellerId": "uuid",
        "categoryId": "uuid",
        "name": "string",
        "description": "string",
        "brand": "string",
        "saleType": "REGULAR|AUCTION",
        "status": "DRAFT|ACTIVE|INACTIVE",
        "statusReason": "SELLER_PAUSED|DISCONTINUED|ADMIN_LOCKED|OTHER | null",
        "specifications": "object",
        "publishedAt": "datetime | null"
      },
      "category": {
        "categoryId": "uuid",
        "parentId": "uuid | null",
        "name": "string",
        "description": "string",
        "level": "int (1..3)",
        "sortOrder": "int",
        "isActive": "boolean"
      },
      "variants": [
        {
          "variantId": "uuid",
          "skuCode": "string",
          "name": "string",
          "price": "decimal | null",
          "status": "ACTIVE|INACTIVE",
          "isDefault": "boolean",
          "attributes": [
            {
              "attrName": "string",
              "attrValue": "string"
            }
          ]
        }
      ],
      "images": [
        {
          "imageId": "uuid",
          "url": "string",
          "altText": "string",
          "isActive": "boolean",
          "sortOrder": "int",
          "variantId": "uuid | null"
        }
      ],
      "rating": {
        "avgRating": "decimal | null",
        "reviewCount": "int"
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Productid or auctionid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `PRODUCT_NOT_FOUND` | Product was not found, has been deleted, or is not accessible. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="9-post-api-v1-products"></a>
### 9. `POST /api/v1/products`

**tạo sản phẩm mới, kèm biến thể đầu, KÈM DANH MỤC**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/products`
- **Mã tham chiếu Excel**: `CAT-01`

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
  "categoryId": "uuid",
  "name": "string",
  "description": "string",
  "brand": "string",
  "specifications": "object",
  "variants": [
  {
  "skuCode": "string",
  "name": "string",
  "price": "decimal (> 0)",
  "currency": "string (mặc định VND)",
  "isActive": "boolean (đúng 1 variant = true, là variant mặc định)",
  "attributes": [
  {
  "attrName": "string",
  "attrValue": "string"
  }
  ]
  }
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
    "data": {
      "productId": "uuid",
      "status": "DRAFT",
      "saleType": "REGULAR",
      "variants": [
        {
          "variantId": "uuid",
          "skuCode": "string"
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
| `400` | `VALIDATION_ERROR` | Name is empty, variants missing, price <= 0, no default variant, or duplicate attribute names. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `CATEGORY_INVALID` | Specified categoryid does not exist, is deactivated, or deleted. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `SELLER_NOT_ALLOWED` | Account is not an approved seller. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `409` | `SKU_CODE_DUPLICATED` | Skucode already exists on an active variant. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `503` | `USER_SERVICE_UNAVAILABLE` | Failed to verify seller account with user service. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="10-patch-api-v1-product-productid-"></a>
### 10. `PATCH /api/v1/product/{productId}`

**seller cập nhật thông tin sản phẩm, danh mục**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/product/{productId}`
- **Mã tham chiếu Excel**: `CAT-02`

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
  {"productId": "uuid"}
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "categoryId": "uuid",
  "name": "string",
  "description": "string",
  "brand": "string",
  "specifications": "object"
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
      "productId": "uuid",
      "categoryId": "uuid",
      "name": "string",
      "description": "string",
      "brand": "string",
      "saleType": "REGULAR|AUCTION",
      "status": "DRAFT|ACTIVE|INACTIVE",
      "specifications": "object",
      "updatedAt": "datetime"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Empty update payload, name is empty, or invalid productid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `CATEGORY_INVALID` | New categoryid does not exist, is deactivated, or deleted. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the owner of this product and not an administrator. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `PRODUCT_NOT_FOUND` | Product was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="11-delete-api-v1-product-productid-"></a>
### 11. `DELETE /api/v1/product/{productId}`

**Xóa sản phẩm**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `DELETE`
- **Đường dẫn**: `/api/v1/product/{productId}`
- **Mã tham chiếu Excel**: `CAT-04`

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
  {"productId": "uuid"}
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
| `400` | `VALIDATION_ERROR` | Productid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the owner of this product and not an administrator. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `PRODUCT_NOT_FOUND` | Product was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `PRODUCT_HAS_ORDER` | Product has variants with associated orders, please deactivate instead. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `PRODUCT_IN_AUCTION` | Product is in an active auction and cannot be modified. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="12-post-api-v1-product-productid-pics"></a>
### 12. `POST /api/v1/product/{productId}pics`

**thêm list ảnh của sản phẩm**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/product/{productId}pics`
- **Mã tham chiếu Excel**: `CAT-12`

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
  {"productId": "uuid"}
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "files": "binary[] (jpg, png, webp)",
  "variantId": "uuid (tùy chọn, không gửi = ảnh chung của sản phẩm)",
  "altText": "string",
  "isActive": "boolean (true = đặt làm ảnh chính)"
  }
  ```

#### 2. Phản hồi thành công (Response)
- **HTTP Status:** `201 Created`
- **Cấu trúc Envelope:** `ApiResponse<PagedData<T>>` (Danh sách có phân trang chuẩn).
- **Payload JSON Mẫu:**
  ```json
  {
    "success": true,
    "statusCode": 201,
    "data": {
      "items": [
        {
          "imageId": "uuid",
          "url": "string",
          "isActive": "boolean",
          "sortOrder": "int"
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
| `400` | `VALIDATION_ERROR` | Files list is empty, variantid does not belong to product, or invalid productid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the owner of this product and not an administrator. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `PRODUCT_NOT_FOUND` | Product was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `IMAGE_LIMIT_EXCEEDED` | Total images for this product cannot exceed 5. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `413` | `FILE_TOO_LARGE` | File size exceeds the maximum allowed limit. | Hiển thị thông báo tương ứng cho người dùng. |
| `415` | `UNSUPPORTED_MEDIA_TYPE` | Unsupported media type, only JPG, PNG, and WEBP formats are allowed. | Hiển thị thông báo tương ứng cho người dùng. |
| `503` | `STORAGE_UNAVAILABLE` | Failed to upload file to object storage service. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="13-get-api-v1-product-productid-pics"></a>
### 13. `GET /api/v1/product/{productId}pics`

**xem các ảnh của sản phẩm**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/product/{productId}pics`
- **Mã tham chiếu Excel**: `CAT-40 (mới)`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  "Authorization": "Bearer <JWT> (optional)"
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  {"productId": "uuid"}
  ```

- **Query Parameters:**
  ```json
  {"variantId": "uuid (tùy chọn, lọc ảnh của một variant)"}
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
          "imageId": "uuid",
          "url": "string",
          "altText": "string",
          "isActive": "boolean",
          "sortOrder": "int",
          "variantId": "uuid | null"
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
| `400` | `VALIDATION_ERROR` | Productid or variantid is not a valid UUID, or variant does not belong to product. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `PRODUCT_NOT_FOUND` | Product was not found, has been deleted, or is not active. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="14-delete-api-v1-product-productid-pics"></a>
### 14. `DELETE /api/v1/product/{productId}pics`

**xóa list ảnh sản phẩm**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `DELETE`
- **Đường dẫn**: `/api/v1/product/{productId}pics`
- **Mã tham chiếu Excel**: `CAT-13`

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
  {"productId": "uuid"}
  ```

- **Query Parameters:**
  ```json
  {"ids": "uuid[] (ImageId, cách nhau dấu phẩy)"}
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
| `400` | `VALIDATION_ERROR` | Image ID list is empty or contains invalid UUIDs. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the owner of this product and not an administrator. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `IMAGE_NOT_FOUND` | One or more images do not exist, were deleted, or do not belong to this product. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="15-get-api-v1-products-productid-totalrating"></a>
### 15. `GET /api/v1/products/{productId}/totalRating`

**xem số sao của 1 sản phẩm, trung bình**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/products/{productId}/totalRating`
- **Mã tham chiếu Excel**: `CAT-31`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  {"productId": "uuid"}
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
      "productId": "uuid",
      "avgRating": "decimal | null",
      "reviewCount": "int"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Productid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `PRODUCT_NOT_FOUND` | Product was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="16-get-api-v1-products-productid-rating"></a>
### 16. `GET /api/v1/products/{productId}/rating`

**tổng số sao, và đánh giá của các người mua trong product**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/products/{productId}/rating`
- **Mã tham chiếu Excel**: `CAT-30 + CAT-31`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  {"productId": "uuid"}
  ```

- **Query Parameters:**
  ```json
  {
  "page": "int",
  "pageSize": "int",
  "rating": "int (1..5, lọc theo số sao)",
  "sortBy": "newest|ratingDesc|ratingAsc (đề xuất)"
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
          "reviewId": "uuid",
          "rating": "int (1..5)",
          "comment": "string",
          "createdAt": "datetime",
          "reviewerName": "string | null"
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
| `400` | `VALIDATION_ERROR` | Invalid pagination, rating outside 1..5, invalid sortBy, or invalid productid format. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `PRODUCT_NOT_FOUND` | Product was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="17-patch-api-v1-product-productid-status"></a>
### 17. `PATCH /api/v1/product/{productId}/status`

**Cập nhật trạng thái sản phẩm**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/product/{productId}/status`
- **Mã tham chiếu Excel**: `CAT-03`

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
  {"productId": "uuid"}
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "status": "DRAFT|ACTIVE|INACTIVE",
  "statusReason": "SELLER_PAUSED|DISCONTINUED|ADMIN_LOCKED|OTHER (bắt buộc khi INACTIVE, các trạng thái khác để trống)"
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
      "productId": "uuid",
      "status": "DRAFT|ACTIVE|INACTIVE",
      "statusReason": "string | null",
      "publishedAt": "datetime | null"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Invalid status, missing statusreason for inactive, or unexpected statusreason. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the product owner, or product is locked by admin (only admin can unlock). | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `PRODUCT_NOT_FOUND` | Product was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `PRODUCT_NOT_PUBLISHABLE` | Cannot activate product without active variants or valid variant prices. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `PRODUCT_IN_AUCTION` | Cannot hide product while it is in an active auction. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="18-post-api-v1-inventory-stock-in"></a>
### 18. `POST /api/v1/inventory/stock-in`

**thêm hàng vào kho, 1 list, kèm số lượng thêm, kèm chủng loại muốn thêm**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/inventory/stock-in`
- **Mã tham chiếu Excel**: `FUL-02 + CAT-24`

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
  "items": [
  {
  "skuId": "uuid",
  "quantity": "int (> 0)"
  }
  ]
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
          "addedQuantity": "int",
          "stockQuantity": "int"
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
| `400` | `VALIDATION_ERROR` | Items list is empty, quantity <= 0, exceeds 100 SKUs, or duplicate SKUs. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | One or more SKUs do not belong to the calling seller products. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `SKU_NOT_FOUND` | One or more SKUs do not exist or have been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `503` | `CATALOG_UNAVAILABLE` | Fulfillment service failed to communicate with catalog service. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="19-post-api-v1-product-productid-variants"></a>
### 19. `POST /api/v1/product/{productId}/variants`

**Thêm biến thể cho sản phẩm**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/product/{productId}/variants`
- **Mã tham chiếu Excel**: `CAT-07`

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
  {"productId": "uuid"}
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "skuCode": "string",
  "name": "string",
  "price": "decimal (> 0)",
  "currency": "string (mặc định VND)",
  "attributes": [
  {
  "attrName": "string",
  "attrValue": "string"
  }
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
    "data": {
      "variantId": "uuid",
      "skuCode": "string",
      "status": "ACTIVE|INACTIVE"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Price must be > 0, attribute names mismatch with existing variants, or invalid productid. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the owner of this product and not an administrator. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `PRODUCT_NOT_FOUND` | Product was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `SKU_CODE_DUPLICATED` | Skucode already exists on an active variant. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `AUCTION_PRODUCT_SINGLE_VARIANT` | Auction products cannot have additional variants. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="20-delete-api-v1-variant-variantid-"></a>
### 20. `DELETE /api/v1/variant/{variantId}`

**xóa 1 biến thể**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `DELETE`
- **Đường dẫn**: `/api/v1/variant/{variantId}`
- **Mã tham chiếu Excel**: `CAT-11`

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
  {"variantId": "uuid"}
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
| `400` | `VALIDATION_ERROR` | Variantid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the owner of this product and not an administrator. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `VARIANT_NOT_FOUND` | Variant was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `LAST_VARIANT` | Cannot delete the last remaining variant of a product. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `VARIANT_HAS_ORDER` | Variant has existing orders, please change status to inactive instead. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="21-patch-api-v1-variant-variantid-"></a>
### 21. `PATCH /api/v1/variant/{variantId}`

**Cập nhật thông tin của sản phẩm biến thể**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PATCH`
- **Đường dẫn**: `/api/v1/variant/{variantId}`
- **Mã tham chiếu Excel**: `CAT-08`

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
  {"variantId": "uuid"}
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "name": "string",
  "price": "decimal (> 0 với REGULAR, để trống với AUCTION)",
  "currency": "string",
  "attributes": [
  {
  "attrName": "string",
  "attrValue": "string"
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
      "variantId": "uuid",
      "productId": "uuid",
      "skuCode": "string",
      "name": "string",
      "price": "decimal | null",
      "currency": "string",
      "status": "ACTIVE|INACTIVE",
      "attributes": [
        {
          "attrName": "string",
          "attrValue": "string"
        }
      ],
      "updatedAt": "datetime"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Empty update payload, invalid price for saletype, or attribute names mismatch. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `PRICE_REQUIRED` | Active regular product must have a valid price. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the owner of this product and not an administrator. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `VARIANT_NOT_FOUND` | Variant was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="22-get-api-v1-variant-variantid-"></a>
### 22. `GET /api/v1/variant/{variantId}`

**xem thông tin chi tiết của 1 biến thể**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/variant/{variantId}`
- **Mã tham chiếu Excel**: `CAT-42 (mới)`

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
  {"variantId": "uuid"}
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
      "variantId": "uuid",
      "productId": "uuid",
      "skuCode": "string",
      "name": "string",
      "price": "decimal | null",
      "currency": "string",
      "status": "ACTIVE|INACTIVE",
      "statusReason": "OUT_OF_STOCK|DISCONTINUED|OTHER | null",
      "isDefault": "boolean",
      "hasOrder": "boolean",
      "attributes": [
        {
          "attrName": "string",
          "attrValue": "string"
        }
      ],
      "images": [
        {
          "imageId": "uuid",
          "url": "string",
          "isActive": "boolean"
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
| `400` | `VALIDATION_ERROR` | Variantid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | You are not the owner of this product and not an administrator. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `VARIANT_NOT_FOUND` | Variant was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="23-post-api-v1-variant-variantid-stock-in"></a>
### 23. `POST /api/v1/variant/{variantId}/stock-in`

**thêm biến thể của sản phảm vào kho**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/variant/{variantId}/stock-in`
- **Mã tham chiếu Excel**: `FUL-02 + CAT-23`

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
  {"variantId": "uuid"}
  ```

- **Request Body (JSON Payload):**
  ```json
  {"quantity": "int (> 0)"}
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
      "skuId": "uuid",
      "addedQuantity": "int",
      "stockQuantity": "int"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Quantity must be greater than zero or invalid variantid format. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Variant does not belong to a product owned by the calling seller. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `VARIANT_NOT_FOUND` | Variant was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `503` | `CATALOG_UNAVAILABLE` | Fulfillment service failed to communicate with catalog service. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="24-get-api-v1-categories"></a>
### 24. `GET /api/v1/categories`

**Xem danh sách danh mục**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/categories`
- **Mã tham chiếu Excel**: `CAT-19`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Query Parameters:**
  ```json
  {
  "parentId": "uuid (tùy chọn, lấy danh mục con của một danh mục)",
  "tree": "boolean (true = dạng cây, false = danh sách phẳng)",
  "keyword": "string"
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
          "categoryId": "uuid",
          "name": "string",
          "level": "int (1..3)",
          "sortOrder": "int",
          "children": "[ ... cùng cấu trúc, chỉ có khi Tree = true ]"
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
| `400` | `VALIDATION_ERROR` | Invalid parentid UUID format or tree parameter must be boolean. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `PARENT_NOT_FOUND` | Parent category was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="25-post-api-v1-categories"></a>
### 25. `POST /api/v1/categories`

**Tạo danh mục mới, có thể truyền danh mục cha, nếu ko truyền nó là cha**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/categories`
- **Mã tham chiếu Excel**: `CAT-15`

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
  "name": "string",
  "description": "string",
  "parentId": "uuid (không gửi = danh mục gốc)",
  "sortOrder": "int"
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
      "categoryId": "uuid",
      "level": "int (1..3)"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Category name is empty or parentid is not a valid UUID. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `CATEGORY_LEVEL_EXCEEDED` | Category hierarchy level exceeds maximum limit of 3. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only administrators can perform this action. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `PARENT_NOT_FOUND` | Parent category was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `CATEGORY_NAME_DUPLICATED` | Category name already exists under the same parent category. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="26-put-api-v1-categories-categoryid-"></a>
### 26. `PUT /api/v1/categories/{categoryId}`

**Cập nhật danh mục**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/categories/{categoryId}`
- **Mã tham chiếu Excel**: `CAT-16`

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
  {"categoryId": "uuid"}
  ```

- **Request Body (JSON Payload):**
  ```json
  {
  "name": "string",
  "description": "string",
  "parentId": "uuid",
  "sortOrder": "int",
  "isActive": "boolean"
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
      "categoryId": "uuid",
      "parentId": "uuid | null",
      "name": "string",
      "description": "string",
      "level": "int (1..3)",
      "sortOrder": "int",
      "isActive": "boolean",
      "updatedAt": "datetime"
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Empty update payload, category name is empty, or invalid category ID. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `CATEGORY_LEVEL_EXCEEDED` | Subtree depth exceeds maximum allowed level of 3 after move. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only administrators can perform this action. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `CATEGORY_NOT_FOUND` | Category was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `404` | `PARENT_NOT_FOUND` | New parent category was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `CATEGORY_NAME_DUPLICATED` | Category name already exists under the same parent category. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `CATEGORY_CYCLE` | Cannot move category into its own descendant hierarchy. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="27-delete-api-v1-categories"></a>
### 27. `DELETE /api/v1/categories`

**Xóa mềm danh mục**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `DELETE`
- **Đường dẫn**: `/api/v1/categories`
- **Mã tham chiếu Excel**: `CAT-17`

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
  {"categoryId": "uuid"}
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
| `400` | `VALIDATION_ERROR` | Categoryid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only administrators can perform this action. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `404` | `CATEGORY_NOT_FOUND` | Category was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |
| `409` | `CATEGORY_HAS_PRODUCTS` | Category still contains products. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `409` | `CATEGORY_HAS_CHILDREN` | Category still contains sub-categories. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |

---

<a id="28-get-api-v1-categories-categoryid-"></a>
### 28. `GET /api/v1/categories/{categoryId}`

**xem danh sách các danh mục con và thông tin 1 danh mục cha**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/categories/{categoryId}`
- **Mã tham chiếu Excel**: `CAT-18`

#### 1. Yêu cầu (Request)
- **Headers:**
  ```http
  Content-Type: application/json
  Authorization: Bearer <JWT_ACCESS_TOKEN>
  x-mock: true  # (Tùy chọn: ép buộc Gateway trả mock fixture trong Seeds/)
  ```

- **Path Parameters:**
  ```json
  {"categoryId": "uuid"}
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
      "category": {
        "categoryId": "uuid",
        "parentId": "uuid | null",
        "name": "string",
        "description": "string",
        "level": "int (1..3)",
        "sortOrder": "int",
        "isActive": "boolean"
      },
      "parent": "{ CategoryId, Name, Level } | null (null nếu là danh mục gốc)",
      "children": [
        {
          "categoryId": "uuid",
          "name": "string",
          "level": "int"
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
| `400` | `VALIDATION_ERROR` | Categoryid is not a valid UUID format. | Hiển thị thông báo tương ứng cho người dùng. |
| `404` | `CATEGORY_NOT_FOUND` | Category was not found or has been deleted. | Không tìm thấy tài nguyên -> Kiểm tra ID gửi lên hoặc tải lại trang. |

---

<a id="29-post-api-v1-product-auction"></a>
### 29. `POST /api/v1/product/auction`

**thêm sản phẩm đấu giá**

- **Quyền hạn (RBAC)**: `Authenticated User` (Yêu cầu JWT Bearer Token)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/product/auction`
- **Mã tham chiếu Excel**: `CAT-43 (mới)`

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
  "categoryId": "uuid",
  "name": "string",
  "description": "string",
  "brand": "string",
  "specifications": "object",
  "variant": {
  "skuCode": "string",
  "name": "string",
  "attributes": [
  {
  "attrName": "string",
  "attrValue": "string"
  }
  ]
  }
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
      "productId": "uuid",
      "status": "DRAFT",
      "saleType": "AUCTION",
      "variant": {
        "variantId": "uuid",
        "skuCode": "string"
      }
    },
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    "timestamp": "2026-10-26T00:00:00Z"
  }
  ```

#### 3. Bảng mã lỗi có thể trả về (RFC 9110 ProblemDetails)
| Mã HTTP | Mã lỗi (errorCode) | Thông điệp tiếng Anh (detail) | Hướng dẫn xử lý Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `VALIDATION_ERROR` | Name is empty, variant definition invalid, or unexpected price field for auction product. | Hiển thị thông báo tương ứng cho người dùng. |
| `400` | `CATEGORY_INVALID` | Specified categoryid does not exist, is deactivated, or deleted. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `SELLER_NOT_ALLOWED` | Account is not an approved seller or has insufficient reputation for auctions. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |
| `409` | `SKU_CODE_DUPLICATED` | Skucode already exists on an active variant. | Dữ liệu bị xung đột hoặc đã tồn tại -> Thông báo người dùng nhập thông tin khác. |
| `503` | `USER_SERVICE_UNAVAILABLE` | Failed to verify seller account with user service. | Hiển thị thông báo tương ứng cho người dùng. |

---

<a id="30-get-api-v1-product-aucction-search"></a>
### 30. `GET /api/v1/product/aucction/search`

**coi đươc các sản phẩm seller đó đã đấu giá, filter**

- **Quyền hạn (RBAC)**: `Seller`
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/product/aucction/search`
- **Mã tham chiếu Excel**: `CAT-06 + CAT-21`

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
  "keyword": "string",
  "categoryId": "uuid",
  "status": "DRAFT|ACTIVE|INACTIVE",
  "brand": "string",
  "sortBy": "newest|priceAsc|priceDesc|rating",
  "sortDir": "asc|desc",
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
| `400` | `VALIDATION_ERROR` | Invalid pagination, sortBy parameter, or invalid categoryid format. | Hiển thị thông báo tương ứng cho người dùng. |
| `401` | `UNAUTHORIZED` | Token is missing, expired, or invalid. | Token hết hạn hoặc không hợp lệ -> Chuyển hướng sang trang Đăng nhập. |
| `403` | `FORBIDDEN` | Only sellers or administrators can perform this action. | Không có quyền thao tác -> Hiển thị cảnh báo từ chối truy cập. |

---
