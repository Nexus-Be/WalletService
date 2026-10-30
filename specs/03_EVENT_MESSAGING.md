# 03 — QUY CHUẨN GIAO TIẾP VÀ BÃI BỎ MESSAGE BROKER (WALLET SERVICE)

> **Trạng thái triển khai (2026-10-30):** 🛑 ĐÃ BÃI BỎ HOÀN TOÀN RABBITMQ & MASSTRANSIT — Toàn bộ cơ chế Consumer, Publisher, Outbox/Inbox tables, và Saga phân tán đã được bãi bỏ. Giao tiếp thanh toán đơn hàng được chuyển dịch sang gọi trực tiếp qua REST API đồng bộ (`POST /api/v1/wallets/checkout/direct`).
> **Nguồn sự thật:** code (`Wallet.Api/Controllers/WalletsController.cs:192-228`).
> **Cập nhật lần cuối:** 2026-10-30.

---

## 1. NGUYÊN TẮC BÃI BỎ MESSAGE BROKER

1. **Không sử dụng RabbitMQ hay MassTransit**:
   - Toàn bộ các package `MassTransit`, `MassTransit.RabbitMQ`, `MassTransit.EntityFrameworkCore` đã được gỡ bỏ khỏi WalletService.
   - Thư mục `Wallet.Application/Consumers` và consumer `StockReservedConsumer` không còn tồn tại.

2. **Chuyển dịch sang REST Endpoint Đồng Bộ Trực Tiếp**:
   - Thay vì lắng nghe `IStockReservedEvent`, service cung cấp API REST đồng bộ:
     `POST /api/v1/wallets/checkout/direct`
   - Client liên service (như `CommerceService`) gọi trực tiếp API này với payload `DirectPaymentHttpRequest` kèm header `Idempotency-Key`.
   - Kết quả thành công hoặc thất bại được phản hồi ngay lập tức trong HTTP response, giải phóng hệ thống khỏi sự phụ thuộc vào message broker và saga rollback.
