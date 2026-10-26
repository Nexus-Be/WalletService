# 03 — SỰ KIỆN BẤT ĐỒNG BỘ QUA RABBITMQ (WALLET SERVICE EVENT MESSAGING)

WalletService đóng vai trò cốt lõi trong bước thanh toán của **Checkout Saga State Machine**.

---

## 1. SỰ KIỆN LẮNG NGHE (CONSUMERS)

1. **`StockReservedEvent`** (Do `CatalogService` phát hành sau khi đã giữ kho ảo thành công):
   - **Xử lý**: 
     - Lấy thông tin `UserId`, `OrderId`, và `TotalAmount` từ sự kiện.
     - Tìm kiếm ví của người dùng trong cơ sở dữ liệu `wallet_db`.
     - **Nếu đủ số dư (`Balance >= TotalAmount`)**: 
       - Trừ tiền ví (`wallet.Deduct(TotalAmount)`).
       - Ghi nhận `WalletTransaction` với type `"ORDER_PAYMENT"`.
       - Bắn sự kiện `PaymentCompletedEvent` lên RabbitMQ qua Transactional Outbox.
     - **Nếu không đủ số dư hoặc xảy ra lỗi**:
       - Không trừ tiền.
       - Bắn sự kiện `PaymentFailedEvent` (kèm lý do) lên RabbitMQ.

---

## 2. SỰ KIỆN PHÁT HÀNH (EVENTS PUBLISHED)

| Tên Sự Kiện | Thời Điểm Kích Hoạt | Payload Dữ Liệu | Services Quan Tâm |
|:---|:---|:---|:---|
| `PaymentCompletedEvent` | Trừ tiền thành công cho đơn hàng | `OrderId`, `TransactionId`, `Amount`, `Timestamp` | `CommerceService` (Đổi trạng thái đơn: PAID), `FulfillmentService` (Tạo vận đơn), `NotificationService` (Gửi thông báo). |
| `PaymentFailedEvent` | Số dư không đủ hoặc thanh toán lỗi | `OrderId`, `Reason`, `Timestamp` | `CommerceService` (Hủy đơn: CANCELLED), `CatalogService` (Bù trừ: nhả kho ảo đã giữ). |

---

## 3. ĐẢM BẢO TÍNH TOÀN VẸN (IDEMPOTENCY & OUTBOX)

1. **Transactional Outbox Pattern**:
   - Sử dụng EF Core Outbox của MassTransit (`AddEntityFrameworkOutbox<WalletDbContext>`).
   - Đảm bảo việc trừ tiền ví và việc phát hành `PaymentCompletedEvent` / `PaymentFailedEvent` nằm chung trong một Database Transaction duy nhất.
2. **Idempotent Consumer**:
   - MassTransit tự động ghi nhận bảng `InboxState` để chặn việc xử lý lặp lại nếu cùng một sự kiện `StockReservedEvent` được gửi tới nhiều lần.

---

## 4. WALLET WORKFLOW EVENTS

The application also consumes `IUserRegisteredEvent` to create an active VND wallet, `IOrderCheckoutRequestedEvent` to debit a buyer's available balance, and `IOrderCancelledEvent` to refund a paid order. Checkout debits are keyed by the order ID; duplicate requests do not debit the wallet twice. `StockReservedEvent` remains supported for the existing checkout flow and publishes its legacy payment result events.

Wallet operation results are published as `IWalletTopupInitiatedEvent`, `IWalletTopupSucceededEvent`, `IWalletTopupFailedEvent`, `IWalletDebitSucceededEvent`, `IWalletDebitFailedEvent`, `IWalletRefundedEvent`, `IWithdrawalRequestedEvent`, `IWithdrawalApprovedEvent`, `IWithdrawalRejectedEvent`, `IDirectPaymentInitiatedEvent`, and `IDirectPaymentFailedEvent`. The external services must use the same event property names and payload types when publishing/consuming these contracts.
