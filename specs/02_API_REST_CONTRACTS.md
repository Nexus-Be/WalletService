# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (WALLET SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-10-30):** ✅ ĐÃ CHUYỂN ĐỔI 100% RESTFUL — Toàn bộ giao tiếp đồng bộ qua RESTful Web API (JSON over HTTP) qua `WalletsController` và `AdminWalletsController`. Bãi bỏ hoàn toàn gRPC, Protobuf (`shop.proto`) và RabbitMQ / MassTransit.
> **Nguồn sự thật:** code (`Wallet.Api/Controllers/WalletsController.cs`, `Wallet.Api/Controllers/AdminWalletsController.cs`).
> **Cập nhật lần cuối:** 2026-10-30.

---

## 1. DANH MỤC ENDPOINTS RESTFUL CHÍNH THỨC

### 1.1. Khách hàng (`WalletsController` — `/api/v1/wallets`)
* `GET /api/v1/wallets/me`: Lấy thông tin ví và số dư khả dụng của người dùng hiện tại (dựa trên header `x-user-id`).
* `POST /api/v1/wallets/topups/vnpay`: Khởi tạo yêu cầu nạp tiền qua VNPay, sinh URL thanh toán.
* `POST /api/v1/wallets/withdraw`: Yêu cầu rút tiền từ số dư ví về tài khoản ngân hàng.
* `POST /api/v1/wallets/transfer`: Chuyển tiền nội bộ giữa hai tài khoản ví người dùng.
* `GET /api/v1/wallets/transactions`: Tra cứu lịch sử biến động số dư của người dùng có phân trang.
* `POST /api/v1/wallets/checkout/direct`: Thanh toán trừ tiền trực tiếp từ ví cho đơn hàng (hỗ trợ header `Idempotency-Key`).
* `GET /api/v1/wallets/vnpay/ipn`: Tiếp nhận và xử lý Instant Payment Notification từ cổng thanh toán VNPay.

### 1.2. Quản trị (`AdminWalletsController` — `/api/v1/admin/wallets`)
* `POST /api/v1/admin/wallets/withdrawals/{id}/process`: Phê duyệt hoặc từ chối yêu cầu rút tiền.
* `POST /api/v1/admin/wallets/adjust`: Điều chỉnh tăng/giảm số dư thủ công có ghi nhận kiểm toán.

---

## 2. PHONG BÌ DỮ LIỆU VÀ MÃ LỖI CHUẨN

* Toàn bộ phản hồi trả về qua envelope `ApiResponse<T>` hoặc `ApiResponse<PagedData<T>>`.
* Mã lỗi chuẩn `SCREAMING_SNAKE_CASE`: `WALLET_NOT_FOUND`, `INSUFFICIENT_BALANCE`, `VAL_INVALID_AMOUNT`, `VAL_INVALID_USER_ID`, `UNAUTHORIZED`, `FORBIDDEN`.
* Thông báo lỗi 100% tiếng Anh viết hoa chữ cái đầu (Sentence case).
