# 03 — QUY CHUẨN GIAO TIẾP VÀ BÃI BỎ MESSAGE BROKER (WALLET SERVICE)

> **Trạng thái triển khai (2026-10-31):** 🛑 ĐÃ BÃI BỎ HOÀN TOÀN RABBITMQ & MASSTRANSIT — Consumer, Publisher, Outbox/Inbox tables và Saga phân tán không còn tồn tại. Thanh toán đơn hàng chạy đồng bộ qua `POST /api/v1/internal/wallets/checkout/direct`.
> **Nguồn sự thật:** code (`Wallet.Api/Controllers/Internal/InternalWalletsController.cs`, `Wallet.Application/Features/Checkout/Commands/CheckoutDirect/`).
> **Cập nhật lần cuối:** 2026-10-31.

---

## 1. NGUYÊN TẮC BÃI BỎ MESSAGE BROKER

1. **Không sử dụng RabbitMQ hay MassTransit**:
   - Toàn bộ các package `MassTransit`, `MassTransit.RabbitMQ`, `MassTransit.EntityFrameworkCore` đã được gỡ bỏ khỏi WalletService.
   - Thư mục `Wallet.Application/Consumers` và consumer `StockReservedConsumer` không còn tồn tại.

2. **Chuyển dịch sang REST Endpoint Đồng Bộ Trực Tiếp**:
   - Thay vì lắng nghe `IStockReservedEvent`, service cung cấp API nội bộ đồng bộ:
     `POST /api/v1/internal/wallets/checkout/direct`
   - Client liên service (như `CommerceService`) gọi trực tiếp API này với payload `CheckoutDirectCommand` kèm header `X-Internal-Service-Key` và `x-user-id`.
   - Kết quả thành công hoặc thất bại được phản hồi ngay lập tức trong HTTP response, không phụ thuộc broker hay saga rollback.

3. **Dispatcher in-process chỉ dùng cho CQRS nội bộ**:
   - Wallet dùng MediatR (`ISender`) cho CQRS nội bộ: mỗi endpoint là một Command/Query + Handler tại `Wallet.Application/Features/{Area}/Commands|Queries/{UseCase}/`; controller chỉ inject `ISender`.
   - Không có consumer/broker. Cấm dùng `INotification`/`IPublisher` để thay thế giao tiếp liên service; giao tiếp liên service vẫn phải là REST đồng bộ.
