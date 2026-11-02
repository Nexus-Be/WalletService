# Tích hợp Wallet: Ghi chú dành cho chủ sở hữu

## Tóm tắt

Commerce hiện có typed HTTP client để gửi yêu cầu thanh toán đơn hàng đến WalletService. Contract Wallet hiện được ghi trong [Wallet Payments API](api/A_ACTIVE/09_WALLET_PAYMENTS_API.md) và [hướng dẫn tích hợp đồng bộ](specs/06_SYNCHRONOUS_SERVICE_INTEGRATION.md). Client gọi ra ngoài và có cấu hình URL khi triển khai, nhưng **không** triển khai hoặc cung cấp một luồng checkout hoàn chỉnh.

## Những gì đã được triển khai

- `Commerce.Application/Interfaces/IWalletClient.cs` khai báo `PayOrderAsync(orderId, amount, accessToken, cancellationToken)`.
- `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` gửi JSON chỉ chứa `amount` đến:
  `POST /api/v1/internal/wallet/orders/{orderId}/payment`
- Client chuyển tiếp token gốc của người mua bằng header `Authorization: Bearer <token>`. Client không gửi ID người mua hoặc internal-service key.
- ID đơn hàng được Wallet dùng làm tham chiếu idempotency. `WalletPaymentResult` giữ lại HTTP status, response body, error code đã phân tích (nếu có) và transaction ID (nếu có). Chỉ `200 OK` mới được xem là thanh toán đã xác nhận.
- Tham số không hợp lệ sẽ gây exception; response lỗi HTTP được trả về dưới dạng kết quả. Exception mạng và timeout được truyền tiếp vì Wallet có thể đã trừ tiền dù Commerce chưa nhận được response.
- Dependency injection đăng ký typed client với timeout 10 giây trong `Commerce.Infrastructure/DependencyInjection.cs`.

## Cấu hình triển khai

Đặt `WALLET_SERVICE_URL` thành base URL của WalletService mà Commerce có thể truy cập. Docker Compose mặc định dùng `http://wallet-service:8008`; khi chạy local ngoài Docker, ứng dụng mặc định dùng `http://localhost:8008/`. Có thể cấu hình `WALLET_SERVICE_URL` dưới dạng biến môi trường hoặc giá trị cấu hình. Đảm bảo container nằm trên network có thể phân giải DNS `wallet-service` và truy cập được cổng `8008`.

Đường dẫn thanh toán là route nội bộ của Wallet: Commerce phải gọi trực tiếp Wallet, không đi qua Gateway công khai. Wallet cần chấp nhận và xác thực token được chuyển tiếp của người mua, đồng thời không cho phép caller bên ngoài truy cập route này.

## Giới hạn hiện tại cần lưu ý

Hiện chưa có nơi nào trong application gọi `IWalletClient` để checkout/thanh toán (`PayOrderAsync` mới chỉ được khai báo và triển khai). Không có endpoint Commerce nào gọi client này, giữ kho trước khi thanh toán, chuyển trạng thái đơn thành đã thanh toán, lưu Wallet transaction ID trả về, retry một giao dịch chưa rõ kết quả, hoặc bù trừ việc giữ kho. Cần triển khai các bước đó trước khi người mua có thể checkout qua Commerce.

Khi bổ sung bước điều phối, chỉ gọi Wallet sau khi giữ kho. Khi timeout, mất kết nối hoặc lỗi máy chủ, giữ đơn ở trạng thái chờ và đối soát/retry với **cùng order ID, token người mua và amount**; không mặc định thanh toán thất bại hoặc giải phóng hàng chỉ vì bị mất response. Xử lý các lỗi nghiệp vụ dứt khoát từ Wallet theo chính sách bù trừ của checkout. Bản thân client hiện không có retry policy.

## Lưu ý về hợp đồng và tài liệu

Tài liệu bàn giao được cung cấp chỉ định route `/api/v1/internal/wallet/orders/{orderId}/payment`, trong khi một số hướng dẫn cũ trong repository nói dùng public path và tạm thời vô hiệu hóa route nội bộ. Phần triển khai hiện tuân theo tài liệu bàn giao Wallet. Trước khi mở rộng tích hợp, hãy xác nhận môi trường Wallet chỉ cung cấp route này trên private service network và cập nhật các hướng dẫn chung còn mâu thuẫn.

Response thành công được tài liệu Wallet mô tả là một transaction object; client đọc trường `id` thành chuỗi. Client vẫn giữ toàn bộ response body để bên gọi có thể kiểm tra thêm trường hoặc điều chỉnh cách phân tích nếu Wallet thay đổi cấu trúc response.

## Giải đáp câu hỏi của chủ sở hữu repository

Các câu trả lời dưới đây phân biệt giữa hợp đồng đã nêu trong handoff, code Commerce hiện tại và thông tin chưa có. Không suy diễn hành vi Wallet từ tên endpoint hoặc các trường trong database Commerce.

### 1. Thanh toán đơn hàng

| Câu hỏi | Điều xác nhận được hiện tại |
|---|---|
| Commerce đang gửi `{ orderId, userId, amount }` đến `POST /api/v1/wallets/checkout/direct` phải không? | **Không phải client hiện tại.** `WalletClient` gọi trực tiếp `POST /api/v1/internal/wallet/orders/{orderId}/payment`; JSON body chỉ có `{ "amount": decimal }`, và buyer JWT được chuyển tiếp bằng `Authorization: Bearer`. `orderId` nằm trong route. Wallet API hiện tại không triển khai `/api/v1/wallets/checkout/direct`. |
| Response thành công có transaction ID không? | Handoff mô tả `200 OK` trả về transaction object với trường `id`; client hiện đọc trường này thành `WalletPaymentResult.TransactionId`. Commerce có `Order.WalletTransactionId`, nhưng hiện chưa có checkout caller lưu giá trị trả về vào database. |
| Idempotency dùng header hay body? Định dạng và TTL là gì? | Với **route nội bộ hiện triển khai**, `orderId` trên route là tham chiếu idempotency; không cần thêm `Idempotency-Key` header hoặc `orderId` trong body. Retry phải giữ nguyên `orderId`, buyer token và amount. Code không đặt TTL cho transaction idempotency; không có căn cứ cho format `quote-{quoteId}` hay TTL cụ thể. |
| Wallet chưa tồn tại và số dư không đủ trả lỗi gì? | Wallet code trả `404 WALLET_NOT_FOUND` nếu buyer chưa có wallet (route payment không tự tạo wallet) và `409 INSUFFICIENT_BALANCE` nếu không đủ tiền. Đây là contract/code hiện tại, chưa phải xác minh runtime trên deployment. |

### 2. Hoàn tiền đơn hàng

Wallet API hiện tại **không triển khai endpoint refund**. Do đó chưa thể xác nhận route, request fields, hoặc liệu Wallet sẽ đối soát theo `orderId`, `subOrderId`, hay `originalTransactionId`.

Commerce có entity `OrderRefund` với các trường liên kết `OrderId`, `SubOrderId`, `WalletTransactionId`, `RefundTransactionId`, `Amount` và `RefundType`; đây chỉ là mô hình dữ liệu phía Commerce, **không chứng minh** Wallet hỗ trợ refund toàn phần, hoàn tiền theo từng Shop, hoặc partial refund. Trường `RefundTransactionId` là `Guid?` trong Commerce, trong khi response payment được handoff mô tả có transaction `id` dạng chuỗi; cần xác nhận kiểu ID Wallet thực tế và quy tắc ánh xạ trước khi triển khai lưu dữ liệu.

Wallet owner cần cung cấp hợp đồng refund gồm: endpoint và quyền truy cập; khóa đối soát/idempotency; ID giao dịch gốc có bắt buộc không; phạm vi refund theo order hay sub-order; giới hạn và cách thực hiện partial/multiple refunds; mã lỗi; response thành công và tên trường refund transaction ID.

### 3. Escrow và seller payout

Wallet hiện atomically debit ví buyer và lưu giao dịch thanh toán theo order ID. Điều này **không xác định** tiền được chuyển vào ví sàn hay phân bổ trực tiếp cho seller, cũng không mô tả escrow, thời điểm giải ngân, hoặc API payout/settlement.

Commerce lưu `SellerReceiveAmount` và `PlatformFeeAmount` trên `SubOrder` và `AuctionOrder`, nhưng các trường này không xác nhận cơ chế quyết toán của Wallet. Không gọi hoặc thiết kế payout dựa trên giả định. Cần Wallet owner xác nhận luồng giữ tiền, quyền sở hữu số dư trong thời gian chờ, điều kiện giải ngân, endpoint payout, idempotency, phí, hoàn tác/thất bại và cách xử lý nhiều seller.

### 4. Đọc số dư ví

Commerce hiện **không có Wallet client gọi API số dư**. Wallet API hiện triển khai public `GET /api/v1/wallets/me`, nhưng không định nghĩa `GET /api/v1/wallets/me/balance`, header `x-user-id`, hay schema riêng cho số dư khả dụng/đóng băng. Wallet xác định chủ ví từ buyer bearer token; không nên gửi hoặc tin cậy `x-user-id` cho mục đích này.

Chưa thể xác nhận response chỉ có `balance` hay phân tách `availableBalance` / `lockedBalance`. Cần Wallet owner cung cấp response schema, ý nghĩa tiền tệ/đơn vị, và xác nhận trường nào được dùng để quyết định khả năng thanh toán. Kể cả khi có API đọc số dư, kết quả chỉ nên dùng để hiển thị/ước lượng; Wallet vẫn phải là nơi quyết định debit nguyên tử để tránh race condition.

### 5. VNPay cho đơn Commerce

Wallet hiện triển khai `POST /api/v1/wallets/topups/vnpay` và callback `GET /api/v1/wallets/vnpay/ipn`, `GET /api/v1/wallets/vnpay/return` cho top-up ví; **không có** contract tạo `paymentUrl` cho order checkout hoặc IPN thanh toán đơn theo WAL-05B.

Commerce có enum `PaymentMethod.VnPay`, nhưng hiện không có client, endpoint hay luồng điều phối VNPay. Cần xác nhận Wallet owner và tài liệu WAL-05B về ai tạo payment URL, ID/reference được ký gửi, nơi nhận IPN/return, xác thực chữ ký, cập nhật trạng thái order và cách xử lý callback trùng/lệch thứ tự trước khi triển khai.

### Các blocker còn lại trước khi mở rộng

1. **Thanh toán order:** WalletService trong repository này đã triển khai `POST /api/v1/internal/wallet/orders/{orderId}/payment`; request dùng UUID order, body chỉ có `amount`, buyer được xác định từ bearer token, response là transaction DTO có `id` dạng chuỗi. Cùng order ID/buyer/amount trả lại transaction cũ; khác buyer hoặc amount trả `409 ORDER_ALREADY_PAID`. Code không đặt TTL cho idempotency record; transaction được lưu trong database. Commerce cần dùng đúng contract này và kiểm thử trên deployment; không có căn cứ cho `checkout/direct`.
2. **Refund, escrow và seller payout:** Chưa có contract hoặc endpoint Wallet. Cần thống nhất quyền truy cập, khóa idempotency, transaction gốc, phạm vi order/sub-order, partial/multiple refund, luồng hold/release, payout, lỗi và đối soát trước khi implement.
3. **Số dư:** Wallet có `GET /api/v1/wallets/me`, trả trường `balance`; không có số dư `available`/`locked` riêng. Đây chỉ là dữ liệu hiển thị, không thay thế quyết định debit nguyên tử của Wallet.
4. **VNPay order checkout:** Các route hiện tại chỉ hỗ trợ top-up ví và callback top-up. Cần contract WAL-05B xác định payment URL, reference, callback owner, chữ ký, cập nhật order và callback trùng/lệch thứ tự.
5. **Phân vùng mạng:** Route nội bộ bị loại khỏi Swagger nhưng điều đó không phải access control. Compose chỉ publish port Wallet lên loopback cho truy cập local; deployment vẫn phải giữ Wallet trên private service network, không proxy route thanh toán nội bộ qua Gateway và không coi JWT buyer là bằng chứng caller là Commerce.

## Kiểm chứng

Trong repository WalletService, `docker compose config --quiet` đã chạy thành công. `Commerce.sln` không có trong repository này nên không thể tái xác minh build của Commerce client tại đây; cần chạy build/test ở repository Commerce sau khi wiring checkout.
