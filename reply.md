Chào team Auction — mình đối chiếu với WalletService hiện tại. **Khuyến nghị Wallet làm chủ tích hợp cổng**, nhưng các API hold/capture bên dưới sẽ là tính năng mới, chưa được hỗ trợ như đang triển khai.

1. **Chủ tích hợp:** Đồng ý về mặt thiết kế: Wallet giữ thông tin tích hợp cổng và quản lý trạng thái thanh toán; Auction giữ trạng thái phiên đấu giá và quyết định kết quả. Hiện Wallet có tích hợp Stripe Checkout, nhưng chưa có quyết định liên service nào trong code xác nhận đây là thỏa thuận chính thức. Tích hợp hiện tại mới được ghi nhận là **test mode**; VNPay initiation, return và IPN đã bị gỡ (`payments.md`, `payments.md`).

2. **Authorization/Hold 8 ngày:** Stripe hỗ trợ manual capture, nhưng luồng Wallet hiện tại tạo Checkout `mode=payment` và không cấu hình `capture_method=manual` (`StripeCheckoutClient.cs`, `StripeCheckoutClient.cs`). Theo [Stripe, giữ tiền trên phương thức thanh toán](https://docs.stripe.com/payments/place-a-hold-on-a-payment-method), authorization online thông thường thường tối đa 7 ngày — **không đủ 8 ngày**. Stripe có extended authorization tối đa 30 ngày cho giao dịch đủ điều kiện, nhưng phụ thuộc thẻ, ngành hàng và cấu hình/tài khoản; chưa xác minh được tài khoản Stripe thực tế của Wallet có đủ điều kiện ([Stripe, extended authorization](https://docs.stripe.com/payments/extended-authorization)). Vì vậy Wallet chưa thể cam kết hold đủ 8 ngày.

3. **Thu ngay rồi hoàn người thua:** Wallet hiện chưa có API gọi Stripe để refund khoản đã thu. Có thể thiết kế thêm, nhưng cần chọn rõ hoàn về **phương thức thanh toán gốc** hay ghi có vào ví Nexus; hai cách này có ý nghĩa và xử lý tài chính khác nhau. Stripe hoàn về phương thức thanh toán gốc, thời gian hiển thị tùy ngân hàng/mạng thẻ và có thể mất vài ngày; phí xử lý ban đầu không được hoàn lại, và phí hoàn tiền tùy điều khoản tài khoản ([Stripe, refunds](https://docs.stripe.com/refunds)). Wallet hiện có luồng hoàn bằng cách cộng lại số dư **cho khoản thanh toán từ ví**, không phải refund giao dịch Stripe (`RefundOrderPaymentCommandHandler.cs`).

4. **Bốn API hold đề xuất:** Hiện trạng:

   | API | Wallet hiện có? |
   |---|---|
   | Tạo hold và trả payment URL | **Chưa có hold.** Có `POST /api/v1/wallets/checkout/direct` trả Stripe Checkout URL, nhưng là thanh toán thông thường; idempotency dựa trên order, người mua và số tiền, không phải khóa Auction gửi cho hold (`WalletsController.cs`). |
   | Capture, chia Seller/Platform hoặc ghi escrow | **Chưa có.** Không có API capture hold hoặc escrow thực sự cho tiền cổng thanh toán. |
   | Release/cancel hold | **Chưa có** API hủy authorization. |
   | Tra trạng thái hold | **Chưa có trạng thái hold.** Có tra trạng thái checkout trực tiếp, nhưng đó không phải trạng thái authorization/hold. |

   Wallet có endpoint settlement ghi có một khoản chỉ định vào ví Seller; đây không phải capture tiền hoặc chia tiền từ hold (`SettleSellerOrderCommandHandler.cs`).

5. **Callback về Auction:** Chưa có callback HTTP từ Wallet sang Auction. Webhook hiện tại đi theo chiều **Stripe → Wallet**; Wallet ghi nhận sự kiện, xử lý trùng lặp và trả lỗi 5xx khi cần Stripe gửi lại, nhưng cơ chế đó không áp dụng cho callback Auction (`WalletsController.cs`, `ProcessStripeWebhookCommandHandler.cs`). Muốn callback sang Auction cần thống nhất riêng cơ chế retry bền vững; hiện có API tra trạng thái thanh toán nhưng chưa có luồng thông báo Auction.

6. **Seller từ chối đơn sau khi đã trả:** Luồng refund hiện có chỉ hoàn bằng cách ghi có vào ví cho khoản thanh toán ví; nó không hoàn Stripe về thẻ và không gộp tự động tiền hàng với tiền cọc đã capture (`WalletsController.cs`, `RefundOrderPaymentCommandHandler.cs`). Muốn hoàn khoản đã thu qua cổng cần bổ sung tích hợp refund và xác định nguồn hoàn.

7. **Chia khoản tịch thu theo tỷ lệ Admin cấu hình:** **Chưa hỗ trợ.** Settlement hiện chỉ ghi có một số tiền chỉ định cho Seller; chưa có cấu hình tỷ lệ chia Seller/Platform hoặc xử lý chia khoản capture.

**Kết luận:** Gửi contract đề xuất được — Wallet sẽ review như một yêu cầu tính năng mới, chưa phải contract mà Wallet có thể cam kết cung cấp ngay. Nên kèm phương thức thanh toán dự kiến, chính sách hoàn tiền, điều kiện đủ chuẩn extended authorization, và yêu cầu về retry/callback. Với hold tiêu chuẩn, mốc 8 ngày hiện không đạt.