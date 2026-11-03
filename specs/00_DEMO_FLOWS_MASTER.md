# 🎯 KỊCH BẢN TỔNG HỢP DEMO HỆ THỐNG PROJECT NEXUS — TÁCH BIỆT PHASE 1 & PHASE 2

> **Kim chỉ nam đồng bộ cho 11 thành viên Project Nexus và Báo cáo trước Mentor**  
> *Phiên bản: 4.5 — Quy hoạch rạch ròi 2 Phase Demo, 100% RESTful API qua Gateway 8080, kiến trúc DB-per-service.*

---

## 📑 MỤC LỤC ĐIỀU HƯỚNG NHANH
1. [Bảng Phân định Phạm vi Demo: Phase 1 vs Phase 2](#-phân-định-phạm-vi-theo-phase-phase-1-vs-phase-2)
2. [Nguyên tắc Kỹ thuật & Danh mục Port / Seed Accounts](#-nguyên-tắc-kỹ-thuật-và-seed-data-dùng-chung)
3. [PHẦN A: DEMO PHASE 1 — HỆ THỐNG CỐT LÕI (CORE LIVE MVP DEMO)](#phần-a-demo-phase-1--hệ-thống-cốt-lõi-core-live-mvp-demo)
   - [A1. Ma trận CRUD 7 Microservices Cốt lõi](#a1-ma-trận-crud-bắt-buộc-của-7-service-phase-1)
   - [A2. Chi tiết 7 Flow Nghiệp vụ Phase 1](#a2-chi-tiết-7-flow-nghiệp-vụ-chuẩn-hóa-phase-1)
   - [A3. Kịch bản Demo Trực chiến Phase 1 (Chạy Live trước Mentor)](#a3-kịch-bản-demo-thực-chiến-phase-1-chạy-live-trước-mentor)
4. [PHẦN B: DEMO PHASE 2 — PHÂN HỆ MỞ RỘNG & HẬU KỲ (EXTENDED BACKLOG DEMO)](#phần-b-demo-phase-2--phân-hệ-mở-rộng--hậu-kỳ-extended-backlog-demo)
   - [B1. Phạm vi & 16 Màn hình FE Backlog Phase 2](#b1-phạm-vi-nghiệp-vụ--16-màn-hình-backlog-phase-2)
   - [B2. Ma trận API & Nghiệp vụ 8 Flow Phase 2](#b2-ma-trận-api--nghiệp-vụ-8-flow-phase-2)
   - [B3. Chi tiết 8 Flow Nghiệp vụ Phase 2 (P2-01 đến P2-08)](#b3-chi-tiết-8-flow-nghiệp-vụ-phase-2-p2-01-đến-p2-08)
   - [B4. Kịch bản Demo Trực chiến Phase 2 (Walkthrough & Mock Guide)](#b4-kịch-bản-demo-thực-chiến-phase-2-walkthrough--mock-guide)
5. [PHẦN C: CHIẾN LƯỢC TRẢ LỜI MENTOR VỀ PHÂN CHIA 2 PHASE](#phần-c-chiến-lược-trả-lời-mentor-về-phân-chia-2-phase)

---

## 📌 PHÂN ĐỊNH PHẠM VI THEO PHASE (PHASE 1 VS PHASE 2)

Hệ thống được quy hoạch phân rã rạch ròi thành 2 Phase để nhóm tự tin ẵm điểm tuyệt đối trước Mentor:

| Hạng mục | **PHASE 1 (CORE MVP LIVE DEMO — CHẠY THẬT)** | **PHASE 2 (EXTENDED SCOPE & BACKLOG DEMO)** |
| :--- | :--- | :--- |
| **Mục tiêu** | Ghép nối 7 Microservices; chỉ trình diễn E2E live cho luồng đã tích hợp và xác minh (FE + Gateway + BE + DB). Checkout Wallet và auction hold còn là integration gap. | Trình bày kiến trúc mở rộng và 8 nghiệp vụ backlog (`F1` - `F17`); không tuyên bố đã triển khai khi chưa có bằng chứng |
| **Hình thức Demo** | **Live theo phạm vi đã xác minh**: UI hoặc Postman qua Gateway 8080; đánh dấu rõ các integration gap, không giả lập thành giao dịch thật | **Walkthrough & Mock API**: Trình chiếu luồng nghiệp vụ qua Postman / Mock / UI Prototype và phân tích kiến trúc |
| **Microservices** | 7 Services: `User:8001`, `Catalog:8002`, `Commerce:8003`, `Auction:8005`, `Fulfillment:8006`, `Notification:8007`, `Wallet:8008` | Kích hoạt thêm `LogService:8004` (Database: 5436) + Hệ thống Worker đối soát & Job hoàn cọc / hoàn kho |
| **7 Chuỗi Flow Cốt lõi (P1)** | 1. Đăng ký OTP, khởi tạo ví ở lần truy cập đầu tiên & Đăng nhập JWT<br>2. Onboarding Seller, OTP Mail DN & Duyệt kho GHN<br>3. Đăng bán sản phẩm DRAFT, Variants, Upload ảnh, Khai kho Fulfillment -> ACTIVE<br>4. Mua hàng E-Commerce, Giữ kho 30p, thanh toán, Trừ kho thật & Vận đơn GHN *(cần xác nhận tích hợp Commerce)*<br>5. Admin Quản trị User, Khóa/Ban vi phạm & Thu hồi Token tức thì<br>6. Quên mật khẩu an toàn & Phòng thủ Brute-force khóa 30p<br>7. Đấu giá Real-time & Chống bắn tỉa 5p; đặt cọc/escrow Wallet chưa được hỗ trợ | — *(Tập trung chạy mượt các chuỗi đã tích hợp; không trình bày hợp đồng Wallet chưa có như tính năng live)* |
| **8 Luồng Mở rộng (P2)** | — *(Bỏ qua rào chắn điểm uy tín và các tính năng phụ để kịp tiến độ)* | **8 Phân hệ Chuyên sâu:**<br>- **P2-01:** Mã giảm giá toàn sàn & shop (`F9`)<br>- **P2-02:** Đánh giá Review 2 chiều & Tính điểm uy tín (`F10`)<br>- **P2-03:** Rút tiền từ ví về ngân hàng (`F8, F3`)<br>- **P2-04:** Sổ cái kép & Đối soát bill lệch (`F13, F16`)<br>- **P2-05:** Tiếp nhận bằng chứng & Phân xử khiếu nại (`F14`)<br>- **P2-06:** Báo cáo & Kiểm duyệt sản phẩm vi phạm (`F15`)<br>- **P2-07:** Xử lý bom hàng, hoàn hàng GHN về kho (`F17`)<br>- **P2-08:** Quản lý session thiết bị, Audit Log tập trung |

---

## ⚡ NGUYÊN TẮC KỸ THUẬT VÀ SEED DATA DÙNG CHUNG

### 3 NGUYÊN TẮC BẮT BUỘC
1. **Điểm gọi duy nhất:** Request từ Client/Postman gửi qua **API Gateway tại port `8080`**. Ngoại lệ là service-to-service: gọi trực tiếp service đích qua private network, không qua Gateway công khai.
2. **Chuẩn giao tiếp:** 100% RESTful API (Đã loại bỏ RabbitMQ và gRPC). Giao tiếp liên service dùng `HttpClient`; các route `/api/v1/internal/...` chỉ dành cho private service network.
3. **Seed Data chung:** Mật khẩu chung cho tất cả tài khoản kiểm thử: `Password123!`
   - `admin@nexus.com` (username: `admin` — Role: `ADMIN`)
   - `staff@nexus.com` (username: `staff` — Role: `STAFF`)
   - `seller@nexus.com` (username: `seller` — Role: `SELLER`)
   - `buyer@nexus.com` (username: `buyer` — Role: `BUYER`)

### DANH MỤC PORT SERVICE & DATABASE
- **API Gateway (YARP):** Port `8080` (Điểm tiếp nhận tập trung)
- **UserService:** Port `8001` | Database: `5433` (`user_db`)
- **CatalogService:** Port `8002` | Database: `5434` (`catalog_db`)
- **CommerceService:** Port `8003` | Database: `5435` (`commerce_db`)
- **AuctionService:** Port `8005` | Database: `5437` (`auction_db`)
- **FulfillmentService:** Port `8006` | Database: `5438` (`fulfillment_db`)
- **NotificationService:** Port `8007` | Database: `5439` (`notification_db`)
- **WalletService:** Port `8008` | Database: `5440` (`wallet_db`)
- **LogService (Phase 2):** Port `8004` | Database: `5436` (`log_db`)
- **Redis Cache:** Port `6379`

---

# PHẦN A: DEMO PHASE 1 — HỆ THỐNG CỐT LÕI (CORE LIVE MVP DEMO)

> Dành cho buổi chạy thực tế: 7 Microservices phối hợp thông suốt qua Gateway 8080.

## A1. MA TRẬN CRUD BẮT BUỘC CỦA 7 SERVICE PHASE 1

### 1. CatalogService (Port 8002)
- **CRUD Danh mục (Category):** Admin tạo (`POST /api/v1/categories`), xem danh sách (`GET /api/v1/categories`), sửa (`PATCH /api/v1/categories/{id}`), xóa/ẩn (`DELETE /api/v1/categories/{id}`).
- **CRUD Sản phẩm (Product):** Seller tạo nháp `DRAFT` (`POST /api/v1/products`), xem chi tiết/danh sách (`GET /api/v1/products`, `GET /api/v1/seller/products`), sửa (`PATCH /api/v1/products/{id}`), xóa mềm (`DELETE /api/v1/products/{id}`).
- **CRUD Biến thể (Variant/SKU):** Thêm variant (`POST /api/v1/products/{id}/variants`), xem chi tiết (`GET /api/v1/variants/{id}`), sửa giá/thuộc tính (`PATCH /api/v1/variants/{id}`), đổi trạng thái (`PATCH /api/v1/variants/{id}/status`), chọn default (`PUT /api/v1/products/{id}/default-variant`), xóa (`DELETE /api/v1/variants/{id}`).
- **Quản lý Hình ảnh (Image):** Upload tối đa 5 ảnh (`POST /api/v1/products/{id}/images`), đổi ảnh chính (`PATCH /api/v1/images/{id}`), xóa ảnh (`DELETE /api/v1/images/{id}`).
- **Kích hoạt Lên sàn (Publish):** Đổi trạng thái sang `ACTIVE` (`PATCH /api/v1/products/{id}/status`). *Rào chắn: Bắt buộc có $\ge 1$ variant active giá > 0 và $\ge 1$ ảnh*.

### 2. CommerceService (Port 8003) - Phân hệ E-Commerce
- **CRUD Giỏ hàng (Cart):** Thêm món (`POST /api/v1/cart/items`), xem giỏ (`GET /api/v1/cart`), sửa số lượng (`PATCH /api/v1/cart/items/{itemId}`), xóa món (`DELETE /api/v1/cart/items/{itemId}`), xóa sạch giỏ (`DELETE /api/v1/cart`).
- **CRUD Đơn hàng (Order):** Tạo đơn checkout (`POST /api/v1/orders`), tra cứu đơn (`GET /api/v1/orders`, `GET /api/v1/orders/{id}`), hủy đơn (`PUT /api/v1/orders/{id}/cancel`).

### 3. AuctionService (Port 8005) - Phân hệ Đấu giá
- **CRUD Phiên đấu giá (Auction):** Seller tạo phiên (`POST /api/v1/auctions`), xem danh sách & chi tiết (`GET /api/v1/auctions`, `GET /api/v1/auctions/{id}`), sửa thông số khi chưa mở (`PUT /api/v1/auctions/{id}`), hủy phiên (`DELETE /api/v1/auctions/{id}/cancel`).
- **CRUD Lượt đặt giá (Bidding):** Đặt giá mới (`POST /api/v1/auctions/{id}/bids`), xem lịch sử đặt giá (`GET /api/v1/auctions/{id}/bids`).

### 4. UserService (Port 8001)
- **CRUD Người dùng (User):** Đăng ký (`POST /api/v1/auth/register`), đăng nhập (`POST /api/v1/auth/login`), xem/sửa hồ sơ (`GET /api/v1/me`, `PATCH /api/v1/me`, `POST /api/v1/me/avatar`), xem danh sách user (`GET /api/v1/admin/user`), khóa/mở tài khoản (`PUT /api/v1/admin/user/{id}`), xóa mềm (`DELETE /api/v1/admin/user/{id}`).
- **CRUD Đơn mở shop (Seller Registration):** Nộp đơn (`POST /api/v1/seller/registration`), xác thực mail OTP (`POST /api/v1/seller/otp/verify`), Staff xem danh sách (`GET /api/v1/admin/seller-registrations`), duyệt (`PUT .../approve`) hoặc từ chối (`PUT .../reject`).

### 5. WalletService (Port 8008)
- **Quản lý Ví & Giao dịch:** Tự động mở ví 0đ qua REST nội bộ (`POST /api/v1/internal/wallets`), nạp tiền ví (`POST /api/v1/wallet/topup`), tra cứu số dư (`GET /api/v1/wallet/balance`), đóng băng cọc đấu giá (Hold), hoàn cọc (Unfreeze), trừ tiền thanh toán (Deduct).

### 6. FulfillmentService (Port 8006)
- **Quản lý Tồn kho & Sổ cái (Inventory):** Khai báo tồn ban đầu (FUL-01), điều chỉnh tăng/giảm tồn kho thực tế (`POST /api/v1/inventory/stock-in` hoặc `PATCH /api/v1/seller/products/{id}/stock`), tra cứu tồn kho khả dụng theo SKU (FUL-03), giữ kho tạm 30p khi tạo đơn (FUL-06), trừ kho thật khi thanh toán (FUL-09), nhả kho khi hủy (FUL-10).
- **Quản lý Vận chuyển GHN:** Tiếp nhận địa chỉ kho Seller (`POST /api/v1/internal/warehouses`), tính phí ship (`POST /api/v1/fulfillments/calculate-fee`), phát hành vận đơn GHN Sandbox khi đơn `PAID` (`POST /api/v1/fulfillments/shipments`), tra cứu hành trình (`GET /api/v1/fulfillments/shipments/{trackingCode}`).

### 7. NotificationService (Port 8007)
- **Quản lý Thông báo:** Gửi email OTP đăng ký & quên pass qua REST nội bộ (`POST /api/v1/internal/notifications/email-otp`), thông báo đơn hàng/đấu giá, xem danh sách thông báo (`GET /api/v1/notifications`), đánh dấu đã đọc (`PATCH /api/v1/notifications/{id}/read`).

---

## A2. CHI TIẾT 7 FLOW NGHIỆP VỤ CHUẨN HÓA (PHASE 1)

### FLOW 1: ĐĂNG KÝ, XÁC THỰC OTP, KHỞI TẠO VÍ VÀ ĐĂNG NHẬP
- **Bước 1 (Đăng ký):** Guest gọi `POST /api/v1/auth/register` (UserService:8001).
  - *Nhánh 1A (Thành công):* Thông tin hợp lệ -> Lưu trạng thái `PENDING_ACTIVATION`. Tạo OTP 6 số lưu Redis (TTL 5 phút). Gọi `POST http://notification-service:8007/api/v1/internal/notifications/email-otp` gửi email xác thực.
  - *Nhánh 1B (Từ chối):* Trùng email (`409 Conflict`), mật khẩu yếu (`400 Bad Request`).
- **Bước 2 (Gửi lại OTP):** `POST /api/v1/auth/otp/resend`. Cooldown 60 giây, tối đa 5 lần/ngày.
- **Bước 3 (Xác thực OTP):** Guest gọi `POST /api/v1/auth/otp/verify`.
  - *Nhánh 3A (Approve):* OTP khớp -> Cập nhật `ACTIVE`, xóa Redis key. WalletService hiện không có route tạo ví nội bộ này. Ví VND số dư 0 được khởi tạo khi người dùng đã đăng nhập gọi `GET /api/v1/wallets/me` lần đầu (qua Gateway); không coi đây là tác vụ đồng bộ trong bước xác thực OTP.
  - *Nhánh 3B (Reject):* OTP sai hoặc quá 5 phút hết hạn -> Báo lỗi; sai quá 5 lần -> Hủy mã OTP chống brute-force.
- **Bước 4 (Đăng nhập):** `POST /api/v1/auth/login`. Trả về Access Token (120 phút) và Refresh Token (7 ngày).

### FLOW 2: ONBOARDING SELLER & DUYỆT KHO GHN
- **Bước 1 (Nộp hồ sơ):** Buyer nộp đơn `POST /api/v1/seller/registration` (hỗ trợ cả nộp từ đầu hoặc nâng hạng). Nhập Tên Shop, Hotline, CCCD, `businessEmail`, Địa chỉ kho GHN.
  - *Tiền kiểm & Rollback:* UserService kiểm tra mã GHN. Nếu lỗi mã địa giới -> Rollback 100%, không lưu kho rác. Nếu hợp lệ -> Lưu hồ sơ `PENDING` tại `user_db` (FulfillmentService hoàn toàn chưa biết gì về kho này). Bắn OTP 6 số về `businessEmail`.
- **Bước 1b (Verify Mail OTP):** `POST /api/v1/seller/otp/verify`. Nhập đúng OTP -> Hồ sơ đổi sang `PENDING_APPROVAL`, xuất hiện ở hàng đợi duyệt của Staff.
- **Bước 2 & 3 (Staff thẩm định):** Staff tra cứu `GET /api/v1/admin/seller-registrations`, xem chi tiết hồ sơ CCCD và kho GHN. *(Bỏ qua kiểm tra điểm uy tín do dời sang Phase 2)*.
- **Bước 4 (Quyết định duyệt):**
  - *Nhánh 4A (Phê duyệt):* Staff gọi `PUT /api/v1/admin/seller-registrations/{id}/approve`. UserService:8001 gọi REST nội bộ `POST http://fulfillment-service:8006/api/v1/internal/warehouses` lưu kho vào bảng `ShopWarehouses` (`fulfillment_db`). Khi Fulfillment trả về 200 OK -> UserService đổi hồ sơ sang `APPROVED` và cấp role `SELLER`.
  - *Nhánh 4B (Từ chối):* `PUT .../reject` bắt buộc truyền `rejectReason`. Hồ sơ đổi sang `REJECTED`, gửi email thông báo lý do. Kho không lưu sang Fulfillment.

### FLOW 3: SELLER ĐĂNG BÁN SẢN PHẨM & TỒN KHO 3 CỘT
- **Bước 1 & 2:** Seller kiểm tra `GET /api/v1/seller/profile`, chọn danh mục `GET /api/v1/categories`.
- **Bước 3 (Khởi tạo DRAFT):** `POST /api/v1/products` (CatalogService:8002). Tạo sản phẩm kèm 1 variant mặc định. Trạng thái ban đầu: `DRAFT` (ẩn với Buyer).
- **Bước 3b (Thêm Variants):** `POST /api/v1/products/{id}/variants` (tối đa 10 variants, mã SKU riêng, giá riêng, bộ thuộc tính phân loại riêng).
- **Bước 3c (Tải ảnh):** `POST /api/v1/products/{id}/images` (tối đa 5 ảnh JPG/PNG/WEBP $\le 5$MB, tự gán ảnh đầu tiên làm ảnh đại diện).
- **Bước 4 (Khai báo Tồn kho sang Fulfillment):** `POST /api/v1/inventory/stock-in` (FulfillmentService:8006 - FUL-01/02). Thiết lập Sổ cái tồn kho ban đầu: `totalStock = initialQty`, `availableStock = initialQty`, `reservedStock = 0`.
- **Bước 5 (Kích hoạt Lên sàn ACTIVE):** `PATCH /api/v1/products/{id}/status` body `{"status": "ACTIVE"}`. Rào chắn: Phải có ít nhất 1 variant active giá > 0 và ít nhất 1 ảnh sản phẩm hợp lệ -> Lên sàn công khai!

### FLOW 4: MUA HÀNG E-COMMERCE, GIỮ KHO TẠM 30P & VẬN ĐƠN GHN
- **Bước 1 & 2:** Buyer xem chi tiết tồn kho khả dụng `GET /api/v1/products/{id}`. Bỏ vào giỏ `POST /api/v1/cart/items` *(Thêm vào giỏ tuyệt đối không giữ/trừ kho)*.
- **Bước 3 (Checkout & Giữ kho tạm):** `POST /api/v1/orders` (CommerceService:8003). Đơn chuyển sang `PENDING_PAYMENT`. Commerce gọi sang FulfillmentService:8006 (`POST .../internal/inventory/hold` - FUL-06) thực hiện **Giữ kho tạm 30 phút**:
  $$\text{reservedStock} += \text{qty}, \quad \text{availableStock} -= \text{qty}, \quad \text{totalStock giữ nguyên}$$
- **Bước 4 (Thanh toán & Vận đơn GHN):** Nếu Commerce đã tích hợp checkout với Wallet, Commerce gọi trực tiếp qua private network `POST /api/v1/internal/wallet/orders/{orderId}/payment`, chuyển tiếp buyer bearer token và gửi body chỉ gồm `amount`. Chỉ `200 OK` mới xác nhận thanh toán; retry phải giữ nguyên `orderId`, buyer và amount. Sau đó Commerce mới đổi đơn sang **`PAID`** và gọi Fulfillment để trừ kho thật/phát hành vận đơn.
  - **Giới hạn cần trình bày:** WalletService không cung cấp endpoint `payments/simulate-webhook` hoặc VNPay thanh toán đơn hàng. Commerce chưa nằm trong repository này và việc tích hợp end-to-end chưa được xác minh; không khẳng định flow này chạy live nếu chưa kiểm thử trên deployment. VNPay top-up ví không phải thanh toán order.
  - *Nhánh hết hạn 30p hoặc Hủy đơn:* `PUT /api/v1/orders/{id}/cancel` -> Fulfillment nhả kho (FUL-10: $\text{reservedStock} -= \text{qty}$, $\text{availableStock} += \text{qty}$).

### FLOW 5: ADMIN QUẢN TRỊ USER, KHÓA/BAN VI PHẠM & THU HỒI TOKEN
- **Bước 1 & 2:** Admin tra cứu danh sách `GET /api/v1/admin/user` và xem chi tiết hồ sơ `GET /api/v1/admin/user/{id}`.
- **Bước 3 (Khóa tài khoản):** `PUT /api/v1/admin/user/{id}` body `{"status": "SUSPENDED"}` hoặc `"BANNED"`. Thu hồi Refresh Token trên DB, vô hiệu hóa Access Token trên Redis -> Người dùng bị văng phiên đăng nhập ngay ở request kế tiếp. Mở khóa đổi status về `ACTIVE`.
- **Bước 4 (Xóa mềm):** `DELETE /api/v1/admin/user/{id}` đánh dấu `isDeleted = true`, vô hiệu hóa đăng nhập nhưng bảo tồn chứng từ kế toán.

### FLOW 6: QUÊN MẬT KHẨU AN TOÀN & PHÒNG THỦ BRUTE-FORCE
- **Bước 1 (Quên mật khẩu):** `POST /api/v1/auth/password/forgot`. Cơ chế **Anti-User Enumeration** luôn trả về HTTP 200 dù email có tồn tại hay không. Nếu tồn tại -> Gửi OTP 6 số qua NotificationService:8007.
- **Bước 2 (Đặt lại mật khẩu):** `PUT /api/v1/auth/password/reset`. Nhập OTP đúng + Mật khẩu mới -> Hash BCrypt lưu DB, xóa OTP Redis, thu hồi toàn bộ Refresh Token trên các thiết bị.
- **Bước 3 (Phòng thủ Brute-force):** Nhập sai mật khẩu liên tiếp 5 lần -> UserService tự động khóa tài khoản tạm thời 30 phút (`HTTP 423 Locked`), chặn không truy vấn DB.

### FLOW 7: ĐẤU GIÁ REAL-TIME, CHỐNG BẮN TỈA & QUYẾT TOÁN (WALLET HOLD CHƯA ĐƯỢC HỖ TRỢ)
- **Bước 1 (Tạo phiên):** Seller tạo phiên `POST /api/v1/auctions` (AuctionService:8005): Giá khởi điểm, bước giá, tiền cọc, giờ bắt đầu/kết thúc.
- **Bước 2 (Nạp ví):** Wallet top-up hiện là `POST /api/v1/wallets/topups/vnpay` và chỉ nạp tiền sau IPN VNPay hợp lệ; đây không phải API nạp giả lập hay API cho auction.
- **Bước 3 (Đặt cọc):** WalletService hiện chưa có API hold/freeze/release tiền đấu giá. Không mô tả `availableBalance`/`frozenBalance` hoặc xác nhận bid dựa trên số dư như chức năng đã triển khai; cần hợp đồng riêng trước khi tích hợp.
- **Bước 4 (Đặt giá bids):** `POST /api/v1/auctions/{id}/bids`. Giá đặt $\ge \text{giá cao nhất hiện tại} + \text{bước giá}$.
  - *Luật Chống bắn tỉa (Anti-Sniping):* Lượt bid hợp lệ xuất hiện trong 5 phút cuối -> Hệ thống tự động gia hạn thêm 5 phút.
- **Bước 5 (Quyết toán hết giờ):**
  - Chốt duy nhất 1 người trả giá cao nhất là người chiến thắng.
  - AuctionService gọi CommerceService tự động sinh đơn hàng chờ thanh toán trong 24 giờ.
  - Việc giải phóng cọc/hoàn tiền chưa có API Wallet tương ứng; đây là phần thiết kế cần chốt contract, không phải hành vi hiện tại.
- **Bước 6 (Thanh toán đơn đấu giá):** Người thắng gọi `POST /api/v1/orders/{orderId}/pay-auction` khấu trừ cọc, phần còn lại thanh toán bằng ví. Quá 24h bỏ cọc -> Tịch thu tiền cọc bồi thường Seller.

---

## A3. KỊCH BẢN DEMO THỰC CHIẾN PHASE 1 (CHẠY LIVE TRƯỚC MENTOR)

### 🎬 Kịch bản P1-1: Demo Luồng Người dùng & Đăng ký Shop (User & Shop Onboarding)
- **Bước 1:** Mở form Đăng ký (`/register`), tạo tài khoản buyer mới. Mở Terminal xem log OTP gửi từ `NotificationService:8007` -> Nhập OTP xác thực -> Kiểm tra trên `wallet_db` ví cá nhân tự động khởi tạo số dư 0 VNĐ.
- **Bước 2:** Đăng nhập với tài khoản `buyer` (`Password123!`).
- **Bước 3:** Vào menu nộp hồ sơ gian hàng (`/buyer/become-seller`). Điền thông tin shop, CCCD, địa chỉ kho GHN hợp lệ -> Nhận mã OTP gửi về `businessEmail` -> Xác thực OTP -> Trạng thái hồ sơ chuyển sang `PENDING_APPROVAL`.
- **Bước 4:** Đăng xuất, đăng nhập tài khoản `staff` (`Password123!`). Vào danh sách duyệt shop (`/admin/seller-registrations`), xem hồ sơ -> Bấm **Phê duyệt (Approve)**. Mở DB chứng minh: UserService đã gọi REST API đồng bộ địa chỉ kho sang `fulfillment_db` bảng `ShopWarehouses`.
- **Bước 5:** Đăng nhập lại tài khoản người bán -> Sidebar tự động mở thêm Kênh bán hàng (`SELLER`), kho hàng GHN đã sẵn sàng.

### 🎬 Kịch bản P1-2: Demo Luồng E-Commerce & Giữ kho tạm 30p (Cart, Inventory Hold & GHN)
- **Bước 1:** Đăng nhập `seller` (`Password123!`). Tạo sản phẩm nháp (DRAFT) có biến thể (Màu sắc, Size), upload 2 ảnh sản phẩm. Khai báo tồn kho ban đầu: `10 cái` trên FulfillmentService (FUL-01). Sau đó bấm Kích hoạt mở bán -> Trạng thái chuyển `ACTIVE`.
- **Bước 2:** Đăng nhập `buyer` (`Password123!`). Xem sản phẩm, bấm Thêm vào giỏ hàng (`/cart`). Mở Swagger/Database Fulfillment chứng minh: Tồn kho vẫn là 10, giỏ hàng không hề giữ kho.
- **Bước 3:** Buyer bấm Checkout tạo đơn hàng -> Đơn hàng sang `PENDING_PAYMENT`. Truy vấn ngay FulfillmentService:8006:
  $$\text{reservedStock} = 1, \quad \text{availableStock} = 9, \quad \text{totalStock} = 10$$
  *(Chứng minh giữ kho tạm thời 30 phút thành công).*
- **Bước 4:** Chỉ thực hiện bước thanh toán nếu checkout đã được tích hợp và xác minh trên deployment. Commerce gọi Wallet theo contract nội bộ, chờ `200 OK`, sau đó mới xử lý `PAID` và gọi Fulfillment trừ kho thật:
  $$\text{totalStock} = 9, \quad \text{reservedStock} = 0, \quad \text{availableStock} = 9$$
  FulfillmentService tự động sinh mã vận đơn GHN Sandbox (ví dụ: `GHN-VN-882391`).

### 🎬 Kịch bản P1-3: Demo Đấu giá Real-time (Wallet Escrow là backlog, chưa demo live)
- **Bước 1:** Đăng nhập `seller` (`Password123!`). Tạo phiên đấu giá cho sản phẩm: Giá khởi điểm 100k, bước giá 20k, tiền cọc 50k, thời lượng 10 phút.
- **Bước 2:** Luồng nạp ví hiện hỗ trợ `POST /api/v1/wallets/topups/vnpay` và cần VNPay IPN hợp lệ. Không demo nút đặt cọc hoặc số dư đóng băng: Wallet chưa có API auction hold/freeze.
- **Bước 3:** Buyer đặt giá 120k -> Thành công. Giả lập tài khoản thứ 2 đặt giá đè lên ở phút chót -> Chứng minh cơ chế Chống bắn tỉa tự động cộng thêm 5 phút vào đồng hồ đếm ngược.
- **Bước 4:** Hết giờ đấu giá -> Phiên đóng `CLOSED`. Việc tạo đơn chờ thanh toán và xử lý cọc cần được trình bày theo contract của Auction/Commerce; Wallet hiện không hỗ trợ tự động unfreeze hoặc refund tiền cọc.

### 🎬 Kịch bản P1-4: Demo Luồng Admin, Quản trị tài khoản & Phòng thủ Brute-force
- **Bước 1:** Đăng nhập `admin` (`Password123!`). Vào danh sách người dùng (`/admin/users`), tìm tài khoản vi phạm.
- **Bước 2:** Admin bấm **Khóa tài khoản (Suspend)**. Chuyển sang trình duyệt của người dùng bị khóa, bấm F5 hoặc gọi API bất kỳ -> Bị văng phiên ngay lập tức, trả về `HTTP 403 Forbidden` do Token đã bị vô hiệu hóa trên Redis.
- **Bước 3:** Đăng xuất ra màn hình Login. Cố tình nhập sai mật khẩu 5 lần liên tiếp -> Hệ thống kích hoạt khóa tạm thời 30 phút (`HTTP 423 Locked`), chứng minh năng lực phòng thủ Brute-force.

---

# PHẦN B: DEMO PHASE 2 — PHÂN HỆ MỞ RỘNG & HẬU KỲ (EXTENDED BACKLOG DEMO)

> Dành cho việc trình diễn kiến trúc hoàn thiện: 8 Use Cases chuyên sâu hậu kỳ, 16 màn hình FE Backlog, LogService tập trung và xử lý tranh chấp tài chính.

## B1. PHẠM VI NGHIỆP VỤ & 16 MÀN HÌNH BACKLOG PHASE 2

Toàn bộ các tính năng dưới đây đã được quy hoạch kiến trúc tại [`00_MASTER_FLOW_TOAN_BO_HE_THONG_THEO_PHASE.md`](file:///c:/Users/ACER/Desktop/obsidian/00_MASTER_FLOW_TOAN_BO_HE_THONG_THEO_PHASE.md) và danh sách màn hình tại [`FE/05_PHASE_2.md`](file:///c:/Users/ACER/Desktop/obsidian/FE/05_PHASE_2.md):

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                        PHASE 2: 8 PHÂN HỆ MỞ RỘNG HẬU KỲ (P2-01 -> P2-08)              │
├──────────────────────────────┬─────────────────────────────┬───────────────────────────┤
│ Phân hệ Khuyến mãi & Uy tín  │ Phân hệ Tài chính & Sổ cái  │ Phân hệ Khiếu nại & Hậu cần│
├──────────────────────────────┼─────────────────────────────┼───────────────────────────┤
│ • P2-01: Voucher Sàn/Shop    │ • P2-03: Rút tiền Ví về NH  │ • P2-05: Khiếu nại & Unbox│
│ • P2-02: Review & Điểm Uy tín│ • P2-04: Sổ cái & Bill lệch │ • P2-06: Kiểm duyệt SP    │
│                              │                             │ • P2-07: Bom hàng & Hoàn kho│
│                              │                             │ • P2-08: LogService & Sec │
└──────────────────────────────┴─────────────────────────────┴───────────────────────────┘
```

### Danh sách 16 màn hình Backlog Frontend Phase 2:
1. `/admin/coupons` — Quản lý mã giảm giá toàn sàn.
2. `/seller/coupons` — Quản lý mã voucher riêng của Shop.
3. `/buyer/orders/[id]/review` — Đánh giá sao & nhận xét sau khi nhận hàng GHN.
4. `/seller/reviews` — Người bán đánh giá uy tín người mua.
5. `/seller/withdrawals` — Seller tạo yêu cầu rút tiền ví doanh thu về ngân hàng.
6. `/staff/withdrawals` — Staff kế toán đối soát & duyệt lệnh chuyển tiền.
7. `/seller/wallet` — Ví doanh thu Seller chuyên biệt.
8. `/admin/ledger` — Sổ cái tài chính bút toán kép.
9. `/admin/ledger/reconciliation/[id]` — Chi tiết kỳ đối soát chênh lệch số dư ngân hàng và ví.
10. `/staff/transactions/discrepancies` — Xử lý giao dịch lệch tiền / treo webhook.
11. `/buyer/reports` — Buyer gửi khiếu nại đính kèm video unbox & ảnh tem GHN.
12. `/staff/reports` — Staff tiếp nhận và làm trọng tài phân xử khiếu nại.
13. `/staff/product-moderation` — Hàng đợi kiểm duyệt sản phẩm vi phạm bị tố cáo.
14. `/seller/reports` — Biểu đồ phân tích doanh thu & tỷ lệ hoàn hủy đơn của shop.
15. `/admin/audit-log` — Nhật ký kiểm toán tập trung từ LogService (Port 8004).
16. `/admin/configs` & `/admin/system-health` — Cài đặt tham số động sàn & giám sát vi dịch vụ.

---

## B2. MA TRẬN API & NGHIỆP VỤ 8 FLOW PHASE 2

| Luồng | Tên Nghiệp vụ | Service Phụ trách | Use Case Excel | APIs & Endpoints Chính | Rào chắn & Bất biến Nghiệp vụ |
| :---: | :--- | :--- | :---: | :--- | :--- |
| **P2-01** | Mã giảm giá & Khuyến mãi | Commerce, Catalog | `F9` (`COM-05..09`) | `POST /api/v1/discounts`<br>`GET /api/v1/discounts/validate`<br>`POST /api/v1/cart/apply-coupon` | Đơn tối thiểu $\ge$ minOrderValue; 1 user chỉ dùng 1 lần; Giữ mã khi pending và nhả mã khi hủy đơn. |
| **P2-02** | Đánh giá & Điểm Uy tín | User, Commerce | `F10` (`USER-08`, `COM-23..26`) | `POST /api/v1/orders/{id}/reviews`<br>`GET /api/v1/users/{id}/reputation` | Chỉ đánh giá khi đơn `COMPLETED`; Cấm tự đánh giá; 5 sao $+1$đ, 1 sao $-2$đ; Dưới 40đ cấm bid; Dưới 20đ khóa shop. |
| **P2-03** | Rút tiền về Ngân hàng (đề xuất) | Wallet, User, Staff | `F8, F3` (`WAL-06..08`, `ADM-15..17`) | Wallet có `POST /api/v1/wallets/withdrawals` và staff review | Wallet tạo yêu cầu Pending và khóa số dư khả dụng; staff có thể approve/reject. Seller verification, bank payout, retry và reconciliation chưa được triển khai; không mô tả đây là chuyển khoản ngân hàng hoàn tất. |
| **P2-04** | Sổ cái & Đối soát bill lệch (đề xuất) | Commerce, Wallet, Admin | `F13, F16` (`COM-18..22`, `WAL-03..05`) | Các route ledger/discrepancy là đề xuất Phase 2, chưa phải API Wallet hiện có | Wallet ghi transaction, chưa có double-entry ledger hoặc đối soát bill lệch; không dùng transaction history để khẳng định sổ cái kép đã triển khai. |
| **P2-05** | Khiếu nại & Tranh chấp (đề xuất) | User, Wallet, Commerce | `F14` (`ADM-10..14`) | Các route dispute/refund là đề xuất Phase 2; Wallet chưa có API refund hoặc escrow | Điều kiện khiếu nại là thiết kế; khóa tiền Seller, refund Buyer và phạt uy tín chưa được Wallet hỗ trợ/cần contract riêng. |
| **P2-06** | Kiểm duyệt Sản phẩm | Catalog, User, Staff | `F15` (`CAT-18..20`, `ADM-05..09`) | `POST /api/v1/products/{id}/reports`<br>`PUT /api/v1/staff/moderation/{id}/action` | Ẩn/xóa sản phẩm vi phạm; Trừ điểm uy tín Shop; Gửi email cảnh cáo người bán. |
| **P2-07** | Bom hàng & Hoàn kho | Fulfillment, Commerce, User | `F17, F11` (`FUL-07..08`, `COM-15..17`) | Webhook GHN `RETURN_TO_ORIGIN`<br>`POST /api/v1/seller/orders/{id}/confirm-return` | GHN giao thất bại 3 lần -> Trừ 5 điểm uy tín Buyer (bom hàng); Seller nhận hàng hoàn -> Hoàn kho `availableStock += qty`. |
| **P2-08** | Cấu hình & LogService | LogService, User, Admin | `F12, F5` (`ADM-20..23`) | `GET /api/v1/admin/audit-logs`<br>`PUT /api/v1/admin/configs`<br>`GET /api/v1/system/health` | LogService:8004 gom log tập trung; Thay đổi tham số sàn realtime không restart server; Đo lường latency container. |

---

## B3. CHI TIẾT 8 FLOW NGHIỆP VỤ PHASE 2 (P2-01 ĐẾN P2-08)

### P2-01: HỆ THỐNG MÃ GIẢM GIÁ & KHUYẾN MÃI (F9)
- **Mục tiêu:** Quản lý voucher giảm giá toàn sàn (Admin tạo) và voucher khuyến mãi riêng của từng Shop (Seller tạo).
- **Quy trình:**
  1. `POST /api/v1/discounts`: Thiết lập mã, % giảm giá (hoặc số tiền cố định), mức giảm tối đa (`maxDiscountAmount`), giá trị đơn tối thiểu (`minOrderValue`), số lượng phát hành (`totalUsageLimit`), thời hạn hiệu lực.
  2. Tại Checkout: Buyer nhập mã coupon. CommerceService kiểm tra điều kiện hợp lệ. Nếu thỏa mãn -> Trừ trực tiếp vào tổng tiền thanh toán.
  3. Cơ chế Giữ mã (Reserve) khi đơn `PENDING_PAYMENT` và Nhả mã (Release) nếu đơn bị hủy hoặc quá 30 phút chưa thanh toán.

### P2-02: ĐÁNH GIÁ ĐỐI TÁC (PEER REVIEW) & TÍNH ĐIỂM UY TÍN (F1, F10)
- **Mục tiêu:** Xây dựng hệ sinh thái tin cậy hai chiều giữa Buyer và Seller, tự động tính điểm uy tín và phân cấp hạng gian hàng.
- **Quy trình:**
  1. Điều kiện tiên quyết: Đơn hàng bắt buộc phải ở trạng thái `COMPLETED` (GHN đã giao thành công và hết hạn đổi trả).
  2. Buyer chấm điểm 1 - 5 sao và viết bình luận (`POST /api/v1/orders/{id}/reviews`).
  3. Thuật toán cập nhật điểm uy tín tự động:
     - Đánh giá 5 sao: $+1$ điểm uy tín
     - Đánh giá 4 sao: $0$ điểm
     - Đánh giá 2 sao: $-1$ điểm
     - Đánh giá 1 sao: $-2$ điểm
  4. Cơ chế phạt vi phạm: Tụt dưới 40 điểm -> Tước quyền tham gia đấu giá; tụt dưới 20 điểm -> Tạm đình chỉ gian hàng.

### P2-03: ĐỀ XUẤT RÚT TIỀN VỀ NGÂN HÀNG (WALLET MỚI HỖ TRỢ MỘT PHẦN)
- **Mục tiêu:** Thiết kế quy trình Seller payout có kiểm duyệt; Wallet hiện hỗ trợ yêu cầu và quyết định staff, chưa chuyển tiền ra ngân hàng.
- **Trạng thái và yêu cầu:** `POST /api/v1/wallets/withdrawals` tạo yêu cầu Pending và khóa số dư khả dụng; staff approve ghi nhận debit hoặc reject nhả khoản khóa. Wallet không xác minh Seller, không gọi ngân hàng, và không có trạng thái/retry/đối soát payout. Cần Wallet owner chốt contract payout/settlement, idempotency, lỗi và reconciliation trước khi triển khai hoặc demo thao tác chuyển khoản.

### P2-04: SỔ CÁI KẾ TOÁN SONG TRÙNG & ĐỐI SOÁT BILL LỆCH (F13, F16)
- **Mục tiêu:** Minh bạch tài chính toàn sàn theo nguyên tắc Kế toán Kép (Double-entry Ledger: Nợ = Có) và xử lý ngoại lệ giao dịch sai lệch.
- **Quy trình:**
  1. Double-entry `LedgerEntries` là mục tiêu đề xuất; Wallet hiện lưu transaction đơn, không cung cấp ledger hai vế hoặc ledger entries cho escrow.
  2. Đề xuất xử lý bill lệch: Khách chuyển thiếu tiền so với đơn hàng (ví dụ: đơn 200k nhưng chuyển 190k) -> Không xác nhận `PAID`; đưa vào hàng đợi đối soát cần được triển khai.
  3. Đề xuất Staff tra cứu ủy nhiệm chi ngân hàng và xử lý phần tiền thiếu/hoàn tiền. Các route discrepancy/refund chưa có trong Wallet API hiện tại.

### P2-05: ĐỀ XUẤT TIẾP NHẬN BẰNG CHỨNG & PHÂN XỬ TRANH CHẤP (WALLET REFUND/ESCROW CHƯA CÓ)
- **Mục tiêu:** Bảo vệ người mua khỏi hàng giả/hỏng và bảo vệ người bán khỏi hành vi báo cáo láo (Anti-Griefing).
- **Quy trình:**
  1. Khiếu nại chỉ được kích hoạt khi đơn hàng ở trạng thái `DELIVERED`.
  2. Buyer nộp đơn khiếu nại bắt buộc kèm bằng chứng: Link video quay rõ 6 mặt kiện hàng còn nguyên tem GHN trước khi khui + Ảnh chụp tem vận đơn.
  3. Escrow là yêu cầu thiết kế, không phải chức năng hiện tại: Wallet chưa có API hold/release funds hoặc contract payout.
  4. Staff Support xem xét bằng chứng:
     - Nếu hàng lỗi thật: Chỉ có thể refund sau khi Wallet owner cung cấp contract; hiện chưa có Wallet refund endpoint.
     - Nếu phát hiện Buyer vu khống: Việc giải phóng tiền/phạt uy tín cũng cần contract và service triển khai; không khẳng định Wallet đang thực hiện.

### P2-06: HỆ THỐNG TIỀN KIỂM / HẬU KIỂM SẢN PHẨM VI PHẠM (F6, F15)
- **Mục tiêu:** Giữ môi trường sàn trong sạch, loại bỏ hàng nhái, hàng cấm hoặc nội dung phản cảm.
- **Quy trình:**
  1. Buyer hoặc cộng đồng bấm nút "Báo cáo sản phẩm vi phạm" trên màn hình chi tiết sản phẩm.
  2. Sản phẩm bị đưa vào danh sách kiểm duyệt của Staff (`/staff/product-moderation`).
  3. Staff kiểm tra: Nếu vi phạm -> Staff gọi API đổi trạng thái sản phẩm sang `BANNED`, ẩn khỏi sàn, trừ điểm uy tín Shop và gửi email cảnh cáo người bán.

### P2-07: XỬ LÝ GIAO HÀNG THẤT BẠI (BOM HÀNG) & HOÀN KHO (F11, F17)
- **Mục tiêu:** Quy trình khép kín khi Buyer từ chối nhận hàng (sau 3 lần bưu tá GHN phát hàng không thành công).
- **Quy trình:**
  1. Bưu tá GHN cập nhật giao hàng thất bại -> GHN bắn Webhook sang FulfillmentService:8006 chuyển trạng thái đơn sang `RETURN_TO_ORIGIN`.
  2. Hệ thống tự động ghi nhận hành vi bom hàng: Tự động trừ 5 điểm uy tín của Buyer.
  3. Kiện hàng vận chuyển hoàn về kho người bán -> Seller vào đơn hàng bấm "Xác nhận đã nhận lại hàng hoàn" -> FulfillmentService tự động hoàn lại số lượng vào tồn kho khả dụng (`availableStock += qty`, `totalStock += qty`).

### P2-08: QUẢN TRỊ CẤU HÌNH ĐỘNG, HEALTH CHECK & AUDIT LOGS (F6, F12)
- **Mục tiêu:** Vận hành và giám sát hệ thống ở cấp độ Doanh nghiệp (Enterprise Operations).
- **Quy trình:**
  1. Admin cấu hình tham số sàn động qua giao diện `/admin/configs` (thay đổi tỷ lệ chiết khấu, bước giá tối thiểu, thời gian hold kho) -> Áp dụng ngay lập tức mà không cần khởi động lại container.
  2. LogService (Port 8004, Database 5436): Thu thập toàn bộ Audit Logs (Ai, làm gì, lúc nào, từ IP nào, dữ liệu trước/sau thay đổi) để phục vụ thanh tra an ninh.
  3. Màn hình System Health: Giám sát uptime, latency và trạng thái kết nối của từng Microservice.

---

## B4. KỊCH BẢN DEMO THỰC CHIẾN PHASE 2 (WALKTHROUGH & MOCK GUIDE)

> Khi Mentor yêu cầu: *"Cho thầy xem Phase 2 các em làm những gì và thiết kế nghiệp vụ thế nào?"*, nhóm trình bày theo đúng 4 kịch bản chuẩn sau:

### 🎬 Kịch bản P2-1: Demo Hệ thống Khuyến mãi Voucher & Đánh giá Uy tín 2 chiều
- **Bước 1 (Voucher):** Mở màn hình Quản lý Voucher của Shop (`/seller/coupons`) hoặc gọi API `POST /api/v1/discounts`. Tạo mã `SALE50K` giảm 50k cho đơn từ 200k. Đăng nhập Buyer, thêm hàng 300k, áp mã `SALE50K` -> Tổng tiền đơn hàng giảm chính xác 50k. Thử áp mã với đơn 150k -> Bị chặn với lỗi `ERR_MIN_ORDER_VALUE_NOT_MET`.
- **Bước 2 (Peer Review & Reputation):** Mở màn hình đơn hàng đã nhận (`/buyer/orders/[id]/review`). Buyer chấm 5 sao kèm hình ảnh thực tế -> Kiểm tra hồ sơ Shop trên DB: Điểm uy tín tăng từ 50 lên 51. Thử kịch bản chấm 1 sao -> Điểm uy tín sụt giảm, chứng minh thuật toán tính điểm tự động.

### 🎬 Kịch bản P2-2: Walkthrough đề xuất payout & sổ cái (không phải demo Wallet live)
- **Bước 1:** Trình bày UI/API mock cho payout. Không gọi Wallet `POST /api/v1/wallets/withdrawals` để mô phỏng lệnh chờ duyệt: route hiện tại trừ tiền ngay, không gửi payout và không thể hiện frozen balance.
- **Bước 2:** Staff approval, đối soát ngân hàng, double-entry ledger và xử lý bill lệch là backlog; chỉ trình bày prototype/contract đề xuất, không tuyên bố đã có API hoặc giao dịch thật.

### 🎬 Kịch bản P2-3: Walkthrough đề xuất khiếu nại/refund & kiểm duyệt sản phẩm
- **Bước 1 (Khiếu nại):** Trình bày quy trình evidence/unbox như prototype. Không khẳng định tạo escrow: Wallet chưa hỗ trợ freeze funds.
- **Bước 2 (Staff phân xử):** Trình bày nhánh refund như đề xuất; Wallet chưa có refund API nên không thể tự động hoàn tiền về Buyer.
- **Bước 3 (Kiểm duyệt sản phẩm):** Vào chi tiết sản phẩm, bấm "Báo cáo hàng giả" -> Staff vào hàng đợi kiểm duyệt (`/staff/product-moderation`), bấm **Khóa sản phẩm vi phạm** -> Sản phẩm biến mất khỏi sàn, Seller nhận email cảnh cáo.

### 🎬 Kịch bản P2-4: Demo Xử lý Bom hàng GHN Hoàn kho & LogService Kiểm toán
- **Bước 1 (Bom hàng & Hoàn kho):** Giả lập Webhook GHN phát hàng không thành công sau 3 lần (`RETURN_TO_ORIGIN`) -> Hệ thống tự động trừ 5 điểm uy tín của Buyer do bùng hàng. Kiện hàng hoàn về kho -> Seller bấm "Xác nhận nhận lại hàng" -> Tồn kho khả dụng tự động phục hồi.
- **Bước 2 (LogService & System Health):** Mở Swagger của `LogService:8004` hoặc màn hình `/admin/audit-log`. Cho Mentor xem nhật ký truy vết tập trung: Ai đã sửa giá sản phẩm, ai đã khóa tài khoản, ai đã duyệt rút tiền, kèm đầy đủ IP và Timestamp.
