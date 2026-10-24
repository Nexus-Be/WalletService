# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (WALLET SERVICE)

---

## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)

**WalletService** chịu trách nhiệm độc quyền về việc quản lý tài khoản ví điện tử, số dư khả dụng, giao dịch tiền tệ nội bộ và thanh toán đơn hàng:
- Quản lý tài khoản ví (`WalletAccount`): Định danh người dùng (`UserId`), số dư hiện tại (`Balance`), đơn vị tiền tệ (`Currency`), trạng thái ví (`Status`).
- Quản lý nhật ký giao dịch (`WalletTransaction`): Ghi nhận chi tiết từng biến động số dư (`DEPOSIT`, `WITHDRAW`, `TRANSFER_IN`, `TRANSFER_OUT`, `ORDER_PAYMENT`), số dư sau giao dịch (`BalanceAfter`).
- Quản lý tài khoản ngân hàng liên kết (`BankAccount`): Phục vụ nạp tiền và rút tiền.
- Tham gia vào quy trình **Checkout Saga**: Lắng nghe sự kiện `StockReservedEvent` từ `CatalogService` để trừ tiền ví của người mua (`DeductBalance`). Bắn `PaymentCompletedEvent` nếu đủ tiền, hoặc `PaymentFailedEvent` nếu thiếu tiền (kích hoạt bù trừ Saga: nhả kho và hủy đơn).
- Cung cấp dịch vụ gRPC Server tại cổng `8008` cho ApiGateway (`WalletApi`).

---

## 2. NHỮNG ĐIỀU CẤM KỴ ĐỐI VỚI AI AGENTS (ANTI-HALLUCINATION GUARDRAILS)

1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**:
   - Tuân thủ nguyên tắc kiến trúc hệ thống: Không sử dụng Repository Pattern bọc ngoài EF Core.
   - Sử dụng trực tiếp `WalletDbContext` và `DbSet<T>` (hoặc interface `IWalletDbContext`) trong tầng Application.
   - Không được tạo `IWalletRepository`, `ITransactionRepository`, `UnitOfWork`.

2. **CÔ LẬP TUYỆT ĐỐI DỮ LIỆU & RANH GIỚI MIỀN (DATABASE-PER-SERVICE)**:
   - `WalletService` chỉ sở hữu cơ sở dữ liệu `wallet_db`.
   - Tuyệt đối **KHÔNG** lưu thông tin giỏ hàng, thông tin kho bãi hay chi tiết sản phẩm vào `WalletService`.
   - Mọi liên kết đơn hàng chỉ lưu mã định danh `OrderId` trong nhật ký giao dịch hoặc event payload.

3. **TUYỆT ĐỐI KHÔNG ĐỂ SỐ DƯ ÂM (NEGATIVE BALANCE)**:
   - Mọi thao tác rút tiền (`Withdraw`), chuyển tiền (`Transfer`), hoặc trừ tiền thanh toán (`Deduct`) bắt buộc phải kiểm tra điều kiện `Balance >= Amount`.
   - Nếu số dư không đủ, bắt buộc ném `InsufficientBalanceException` và không cho phép giao dịch hoàn tất.

4. **TUYỆT ĐỐI KHÔNG THAY ĐỔI CỔNG 8008 VÀ DATABASE 5440**:
   - Service gRPC chạy tại `http://localhost:8008`.
   - Chuỗi kết nối DB trỏ tới `localhost:5440/wallet_db` với mật khẩu mặc định `MatkhaucuaTui123@`.

5. **TUYỆT ĐỐI KHÔNG TỰ Ý SỬA PROTOBUF MÀ KHÔNG ĐỒNG BỘ GATEWAY**:
   - RPC methods và message formats phải khớp 100% với `Contracts/shop.proto` tại ApiGateway (`WalletApi`).
