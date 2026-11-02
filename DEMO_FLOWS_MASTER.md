# KỊCH BẢN

## TÓM TẮT NHANH DÀNH CHO NHÓM (ĐỌC ĐỂ BIẾT CẦN LÀM GÌ)

Tài liệu này là kim chỉ nam đồng bộ cho 11 thành viên Project Nexus nhằm ghép nối hệ thống và chuẩn bị demo trước Mentor.

3 NGUYÊN TẮC KỸ THUẬT BẮT BUỘC:
- Điểm gọi duy nhất: Mọi request từ Client/Postman đều gửi qua API Gateway tại port 8080 (không gọi trực tiếp vào port nội bộ của các service).
- Tài khoản dùng chung: 4 tài khoản seed: `admin`, `staff`, `seller`, `buyer` (mật khẩu chung: `Password123!`).
- Nguồn tra cứu API gốc: Toàn bộ API lấy từ file https://docs.google.com/spreadsheets/d/17WV9gDGLpBVX06uo_ULkA-M9pp5R8lxnQdsIN9AmjEI/edit?gid=10799403#gid=10799403  API.xlsx` (Sheet 1) + FUNCTION (2-7). LogService tạm thời gác lại để triển khai sau, tập trung 7 service cốt lõi.

3 VIỆC MỖI THÀNH VIÊN CẦN LÀM NGAY:
1. Đối chiếu Service của mình tại Mục 2 (Ma trận CRUD): Đảm bảo các chức năng bắt buộc của service mình phụ trách đã map đúng endpoint.
2. Kiểm tra nghiệp vụ tại Mục 3 (7 Flow chi tiết): Đảm bảo code của mình xử lý đủ cả 2 trường hợp: Nhánh A (Chấp thuận / Approve) và Nhánh B (Từ chối / Reject).
3. Tập dượt kịch bản tại Mục 4 (Kịch bản Demo): Chạy thử theo đúng thứ tự 5 phần để báo cáo mạch lạc, không bị khớp.

---

## 1. THÔNG TIN HỆ THỐNG VÀ TÀI KHOẢN KHỞI TẠO (SEED DATA)

### Danh mục tài khoản kiểm thử (Seed Accounts)
- admin: Password123! (Vai trò: Admin)
- staff: Password123! (Vai trò: Staff)
- seller: Password123! (Vai trò: Seller)
- buyer: Password123! (Vai trò: Buyer)

### Danh mục Port Service & Database
- API Gateway: Port 8080 (Điểm tiếp nhận request duy nhất từ Frontend/Client)
- UserService: Port 8001 (Database: 5433 - user_db)
- CatalogService: Port 8002 (Database: 5434 - catalog_db)
- CommerceService: Port 8003 (Database: 5435 - commerce_db)
- AuctionService: Port 8005 (Database: 5437 - auction_db)
- FulfillmentService: Port 8006 (Database: 5438 - fulfillment_db)
- NotificationService: Port 8007 (Database: 5439 - notification_db)
- WalletService: Port 8008 (Database: 5440 - wallet_db)
- LogService: Port 8004 (Database: 5436 - log_db, Triển khai sau)

---

## 2. MA TRẬN CRUD BẮT BUỘC CỦA TỪNG SERVICE

### 1. CatalogService (Port 8002)
- CRUD Danh mục sản phẩm (Category): Admin tạo (POST /api/v1/categories), xem danh sách (GET /api/v1/categories), sửa (PUT /api/v1/categories/{id}), xóa/ẩn (DELETE /api/v1/categories/{id}).
- CRUD Sản phẩm (Product): Seller đăng bán (POST /api/v1/seller/products), xem chi tiết/danh sách (GET /api/v1/products), cập nhật giá/thông tin (PUT /api/v1/seller/products/{id}), ngừng kinh doanh (DELETE /api/v1/seller/products/{id}).
- Quản lý Tồn kho: Cập nhật tăng/giảm tồn kho thực tế (PATCH /api/v1/seller/products/{id}/stock). --> của fulfillment ?

### 2. CommerceService (Port 8003) - Phân hệ E-Commerce
- CRUD Giỏ hàng (Cart): Thêm vào giỏ (POST /api/v1/cart/items), xem giỏ và tính tổng tiền (GET /api/v1/cart), sửa số lượng (PATCH /api/v1/cart/items/{itemId}), xóa món (DELETE /api/v1/cart/items/{itemId}), xóa sạch giỏ (DELETE /api/v1/cart).
- CRUD Đơn hàng (Order): Tạo đơn hàng checkout (POST /api/v1/orders), tra cứu đơn hàng (GET /api/v1/orders, GET /api/v1/orders/{id}), hủy đơn (PUT /api/v1/orders/{id}/cancel).

### 3. AuctionService (Port 8005) - Phân hệ Đấu giá
- CRUD Phiên đấu giá (Auction): Seller tạo phiên (POST /api/v1/auctions), xem danh sách và chi tiết (GET /api/v1/auctions, GET /api/v1/auctions/{id}), sửa cấu hình trước giờ mở (PUT /api/v1/auctions/{id}), hủy phiên (DELETE /api/v1/auctions/{id}/cancel).
- CRUD Lượt đặt giá (Bidding): Đặt giá mới (POST /api/v1/auctions/{id}/bids), xem lịch sử đặt giá (GET /api/v1/auctions/{id}/bids).

### 4. UserService (Port 8001)
- CRUD Người dùng (User): Đăng ký hoặc Admin tạo tài khoản (POST /api/v1/auth/register, POST /api/v1/admin/user), xem danh sách và chi tiết (GET /api/v1/admin/user, GET /api/v1/admin/user/{id}), cập nhật hồ sơ/khóa/mở/gán role (PUT /api/v1/admin/user/{id}), xóa mềm tài khoản (DELETE /api/v1/admin/user/{id}).
- CRUD Đơn mở shop (Seller Registration): Buyer nộp đơn (POST /api/v1/seller/registration), Staff xem danh sách (GET /api/v1/admin/seller-registrations), duyệt (PUT .../approve) hoặc từ chối (PUT .../reject).

### 5. WalletService (Port 8008)
- Quản lý Ví & Giao dịch: Tự động mở ví khi kích hoạt, nạp tiền (POST /api/v1/wallet/topup), tra cứu số dư/lịch sử (GET /api/v1/wallet/balance), đóng băng cọc đấu giá (Hold), hoàn cọc (Unfreeze), trừ tiền thanh toán (Deduct).

### 6. FulfillmentService (Port 8006)
- Quản lý Vận chuyển GHN: Tính phí ship (POST /api/v1/fulfillments/calculate-fee), tạo vận đơn GHN khi thanh toán thành công (POST /api/v1/fulfillments/shipments), tra cứu hành trình (GET /api/v1/fulfillments/shipments/{trackingCode}), cập nhật trạng thái từ webhook GHN.

### 7. NotificationService (Port 8007)
- Quản lý Thông báo: Bắn email OTP, thông báo đơn hàng/đấu giá, xem thông báo cá nhân (GET /api/v1/notifications), đánh dấu đã đọc (PATCH /api/v1/notifications/{id}/read).

---

## 3. CHI TIẾT 7 FLOW NGHIỆP VỤ CHUẨN HÓA

### FLOW 1: ĐĂNG KÝ, XÁC THỰC OTP, KHỞI TẠO VÍ VÀ ĐĂNG NHẬP

Mục tiêu: Chuyển hóa khách vãng lai thành Buyer có tài khoản Active, tự động mở ví cá nhân và cấp JWT Token.

Bước 1: Đăng ký tài khoản mới
- Người thực hiện: Guest
- API Gateway: POST /api/v1/auth/register
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Nhập họ tên, email, mật khẩu (tối thiểu 8 ký tự, gồm chữ hoa, chữ thường, số, ký tự đặc biệt).
  - Nhánh 1A (Chấp thuận / Success): Thông tin hợp lệ -> Lưu user ở trạng thái PENDING_ACTIVATION. Tạo mã OTP 6 số lưu vào Redis với TTL 5 phút, bắn sự kiện UserRegistered sang NotificationService:8007 qua RabbitMQ để gửi email xác thực.
  - Nhánh 1B (Từ chối / Reject): Email đã tồn tại trong hệ thống (HTTP 409 Conflict) hoặc mật khẩu không đúng định dạng an toàn (HTTP 400 Bad Request).

Bước 2: Yêu cầu gửi lại OTP
- Người thực hiện: Guest
- API Gateway: POST /api/v1/auth/otp/resend
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Nhánh 2A (Chấp thuận / Success): Đã qua 60 giây kể từ lần gửi trước -> Hệ thống sinh mã OTP mới lưu Redis (TTL 5 phút) và gửi email.
  - Nhánh 2B (Từ chối / Reject): Gửi lại khi chưa đủ 60 giây cooldown -> Trả về HTTP 429 Too Many Requests; Vượt quá giới hạn 5 lần/ngày -> Khóa chức năng gửi lại trong ngày.

Bước 3: Xác thực OTP kích hoạt tài khoản
- Người thực hiện: Guest
- API Gateway: POST /api/v1/auth/otp/verify
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Nhánh 3A (Xác thực thành công / Approve): Nhập đúng mã OTP 6 số -> Cập nhật trạng thái user sang ACTIVE, xóa ngay key Redis (đảm bảo mã chỉ dùng 1 lần duy nhất). UserService bắn sự kiện UserActivatedEvent sang WalletService:8008 qua RabbitMQ để tự động khởi tạo ví cá nhân mặc định số dư 0 VNĐ.
  - Nhánh 3B (Từ chối xác thực / Reject): Nhập sai mã OTP -> Báo lỗi mã không chính xác; Nhập sai quá 5 lần liên tiếp -> Redis hủy mã OTP ngay lập tức để chống brute-force, từ chối kích hoạt, bắt buộc phải yêu cầu gửi lại mã mới.
  - Nhánh 3C (Hết hạn / Expired): Quá 5 phút chưa xác thực -> Trả về HTTP 400 OTP Expired.

Bước 4: Đăng nhập hệ thống
- Người thực hiện: buyer (Password123!)
- API Gateway: POST /api/v1/auth/login
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Nhánh 4A (Đăng nhập thành công / Success): Thông tin chính xác và tài khoản ACTIVE -> Trả về Access Token (thời hạn 120 phút) và Refresh Token (lưu DB thời hạn 7 ngày).
  - Nhánh 4B (Từ chối đăng nhập / Reject):
    + Tài khoản chưa xác thực OTP (PENDING_ACTIVATION) -> Trả về HTTP 403 Forbidden kèm thông báo yêu cầu kích hoạt.
    + Tài khoản đang bị kỷ luật (SUSPENDED hoặc BANNED) -> Trả về HTTP 403 Forbidden.
    + Nhập sai mật khẩu liên tiếp 5 lần -> Kích hoạt khóa tạm thời 30 phút, trả về HTTP 423 Locked.

---

### FLOW 2: NÂNG HẠNG TÀI KHOẢN VÀ THẨM ĐỊNH GIAN HÀNG SELLER --> FLOW ĐĂNG KÝ SELLER NÊN TÁCH RA Ở LÚC ĐĂNG KÍ TÀI KHOẢN LUÔN !!

Mục tiêu: Buyer nộp hồ sơ xin mở shop, Staff thẩm định và Admin/Staff ra quyết định Phê duyệt (Approve) hoặc Từ chối (Reject).

Bước 1: Buyer nộp hồ sơ mở gian hàng
- Người thực hiện: buyer (Password123!)
- API Gateway: POST /api/v1/seller/registration
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Điền thông tin: Tên shop, hotline, CCCD, địa chỉ kho lấy hàng (mã tỉnh/huyện/xã chuẩn GHN).
  - Nhánh 1A (Chấp thuận nộp đơn / Success): Tài khoản đang ACTIVE, chưa có quyền SELLER và chưa có đơn nào đang chờ -> Hồ sơ chuyển sang trạng thái PENDING_APPROVAL.
  - Nhánh 1B (Từ chối tiếp nhận / Reject): đã là SELLER, hoặc đang có một đơn khác chưa được xử lý -> Trả về HTTP 400 Bad Request.

Bước 2: Staff tra cứu danh sách hồ sơ chờ duyệt
- Người thực hiện: staff (Password123!)
- API Gateway: GET /api/v1/admin/seller-registrations
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Staff xem danh sách hàng đợi các đơn PENDING_APPROVAL, lọc theo thời gian tạo hoặc tìm kiếm theo tên shop.

Bước 3: Staff kiểm tra chi tiết hồ sơ
- Người thực hiện: staff (Password123!)
- API Gateway: GET /api/v1/admin/user/{userId}
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Thẩm định tính hợp lệ của CCCD, địa chỉ kho và điểm uy tín người bán. --> CÓ VẤN ĐỀ !!!!

Bước 4: Quyết định Phê duyệt (Approve) hoặc Từ chối (Reject)
- Người thực hiện: staff / admin (Password123!)
- Tình huống và Điều kiện thực tế:
  - Nhánh 4A (Phê duyệt / Approve):
    + API Gateway: PUT /api/v1/admin/seller-registrations/{id}/approve
    + Service: UserService:8001
    + Xử lý: Trạng thái hồ sơ đổi sang APPROVED. Tài khoản được gán bổ sung vai trò SELLER và kích hoạt ShopProfile. Bắn sự kiện sang FulfillmentService:8006 để đồng bộ địa chỉ kho hàng phục vụ tính phí ship và lấy hàng GHN.
  - Nhánh 4B (Từ chối / Reject):
    + API Gateway: PUT /api/v1/admin/seller-registrations/{id}/reject
    + Service: UserService:8001
    + Xử lý: Bắt buộc truyền lý do từ chối (rejectReason). Trạng thái hồ sơ đổi sang REJECTED. Hệ thống gửi email thông báo lý do cụ thể để người dùng chỉnh sửa hồ sơ nộp lại.

---

### FLOW 3: SELLER ĐĂNG BÁN SẢN PHẨM VÀ QUẢN LÝ TỒN KHO

Mục tiêu: Seller thiết lập sản phẩm kinh doanh và kiểm soát tính toàn vẹn của số lượng tồn kho.

Bước 1: Kiểm tra thông tin Shop & Kho hàng
- Người thực hiện: seller (Password123!)
- API Gateway: GET /api/v1/seller/profile
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Kiểm tra trạng thái hoạt động của gian hàng và địa chỉ kho GHN đã được phê duyệt.

Bước 2: Lấy danh mục sản phẩm hợp lệ
- Người thực hiện: seller (Password123!)
- API Gateway: GET /api/v1/categories
- Service: CatalogService:8002
- Tình huống và Điều kiện thực tế:
  - Truy vấn danh mục đang mở để chọn đúng categoryId.

Bước 3: Đăng bán sản phẩm mới --> CHỈNH SỐ LƯỢNG TỒN KHO ?
- Người thực hiện: seller (Password123!)
- API Gateway: POST /api/v1/seller/products
- Service: CatalogService:8002
- Tình huống và Điều kiện thực tế:
  - Nhánh 3A (Tạo thành công / Approve): Điền đủ thông tin, giá bán > 0, categoryId hợp lệ, 
  
  số lượng nhập ban đầu totalStock > 0 -> Hệ thống khởi tạo sản phẩm với bất biến:
    totalStock = số lượng nhập ban đầu
    availableStock = totalStock
    reservedStock = 0
------> CỦA FULFULLMENT !!

  - Nhánh 3B (Từ chối tạo / Reject): Giá bán <= 0, categoryId không tồn tại, hoặc kích thước/trọng lượng GHN không hợp lệ -> Trả về HTTP 400 Bad Request.

Bước 4: Điều chỉnh tồn kho thực tế
- Người thực hiện: seller (Password123!)
- API Gateway: PATCH /api/v1/seller/products/{id}/stock
- Service: CatalogService:8002
- Tình huống và Điều kiện thực tế:
  - Nhánh 4A (Cập nhật thành công / Success): Tăng số lượng nhập thêm hoặc giảm tồn kho hợp lệ -> Cập nhật totalStock và availableStock tương ứng.
  - Nhánh 4B (Từ chối điều chỉnh / Reject): Yêu cầu giảm totalStock xuống thấp hơn mức reservedStock đang bị giam bởi các đơn hàng đang chờ thanh toán -> Trả về HTTP 400 Bad Request để bảo vệ các đơn hàng đang xử lý.

--------> CỦA FULFILMENT
---

### FLOW 4: BUYER MUA HÀNG, GIỮ KHO TẠM, THANH TOÁN WEBHOOK VÀ TẠO VẬN ĐƠN GHN (E-COMMERCE) --> CỦA FULFILMENT VÀ CATALOG ??

Mục tiêu: Đảm bảo quy trình mua bán hàng có giữ kho tạm thời, thanh toán qua Webhook ngân hàng và phát hành vận đơn GHN.

Bước 1: Xem chi tiết sản phẩm và tồn kho
- Người thực hiện: buyer (Password123!)
- API Gateway: GET /api/v1/products/{id}
- Service: CatalogService:8002 --> 
- Tình huống và Điều kiện thực tế:
  - Xem thông tin giá (CATALOG) và tồn kho khả dụng (availableStock). Nếu availableStock == 0, giao diện tự động ẩn nút mua hàng. (FULFILMENT)


Bước 2: Thêm sản phẩm vào giỏ hàng
- Người thực hiện: buyer (Password123!)
- API Gateway: POST /api/v1/cart/items
- Service: CommerceService:8003
- Tình huống và Điều kiện thực tế:
  - Bất biến nghiệp vụ: Thêm vào giỏ hàng KHÔNG giữ kho, KHÔNG trừ bất kỳ loại tồn kho nào (giỏ hàng chỉ là danh sách ý định mua).

Bước 3: Đặt hàng - Checkout (Quyết định Giữ kho tạm)
- Người thực hiện: buyer (Password123!)
- API Gateway: POST /api/v1/orders
- Service: CommerceService:8003
- Tình huống và Điều kiện thực tế:
  - Nhánh 3A (Chấp thuận tạo đơn / Approve): Tồn kho khả dụng đủ (FULFILMENT) (availableStock >= số_lượng_mua) -> Tạo đơn ở trạng thái PENDING_PAYMENT. Gọi CatalogService:8002 thực hiện GIỮ KHO TẠM trong 30 phút (FULFILMENT): --> KHI NÀO BƯỚC VÀO KHÚC THANH TOÁN THÌ MỚI CHO GIỮ KHO TẠM !!
    reservedStock += số_lượng_mua
    availableStock -= số_lượng_mua
    totalStock giữ nguyên
  - Nhánh 3B (Từ chối tạo đơn / Reject): Tồn kho khả dụng không đủ (do người khác vừa mua trước) -> Trả về HTTP 400 Out of Stock, từ chối tạo đơn hàng.


--> BƯỚC CUỐI CÙNG KHI COMMERCE SERVICE GỬI THÔNG TIN VỀ ĐƠN HÀNG, SỐ LƯỢNG, ĐỊA CHỈ,.... --> GỬI SỐ TIỀN CHO ORDER VỀ CHO WALLET --> SAU KHI WALLET SUCCESS THÌ GỬI VỀ FULFILMENT ĐỂ TIẾN HÀNH TRỪ SL TRONG KHO HÀNG 

Bước 4: Xử lý kết quả thanh toán qua Webhook ngân hàng
- Người thực hiện: Webhook hệ thống / Ngân hàng (Giả lập thanh toán)
- API Gateway: POST /api/v1/payments/simulate-webhook
- Service: CommerceService:8003 --> WALLET SERVICES
- Tình huống và Điều kiện thực tế:
  - Nhánh 4A (Thanh toán thành công / Approve):
    + Điều kiện: Số tiền thanh toán khớp với giá trị đơn hàng và đơn đang ở PENDING_PAYMENT.
    + Xử lý: Đơn hàng đổi sang trạng thái PAID.
    + Trừ kho thực tế trên CatalogService:8002:
      totalStock -= số_lượng_mua
      reservedStock -= số_lượng_mua
    + Gọi FulfillmentService:8006 tự động phát hành vận đơn GHN, nhận mã trackingCode và chuyển đơn sang PROCESSING.
  - Nhánh 4B (Thanh toán thất bại / Hết hạn / Reject):
    + Quá 30 phút buyer không hoàn tất thanh toán hoặc Buyer chủ động hủy đơn qua API: PUT /api/v1/orders/{id}/cancel.
    + Xử lý: Đơn hàng đổi sang trạng thái CANCELLED.
    + Hoàn trả kho tạm trên CatalogService:8002:
      reservedStock -= số_lượng_mua
      availableStock += số_lượng_mua
      totalStock giữ nguyên

---

### FLOW 5: ADMIN QUẢN TRỊ TÀI KHOẢN, KHÓA/MỞ KHÓA VÀ XÓA MỀM

Mục tiêu: Admin thực thi các biện pháp quản trị người dùng (Tra cứu, Khóa/Mở khóa tài khoản, Xóa mềm).

Bước 1: Tra cứu danh sách tài khoản
- Người thực hiện: admin (Password123!)
- API Gateway: GET /api/v1/admin/user
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Tra cứu người dùng có phân trang, lọc theo vai trò (BUYER, SELLER, STAFF) và trạng thái tài khoản.

Bước 2: Xem chi tiết hồ sơ và lịch sử uy tín --> UY TÍN LÀM SAU !!
- Người thực hiện: admin (Password123!)
- API Gateway: GET /api/v1/admin/user/{userId}
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Xem biến động điểm uy tín (UY TÍN LÀM SAU !!), lịch sử vi phạm đơn hàng hoặc gian lận đấu giá.

Bước 3: Quản trị trạng thái (Khóa / Mở khóa tài khoản)
- Người thực hiện: admin (Password123!)
- API Gateway: PUT /api/v1/admin/user/{userId}
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Nhánh 3A (Khóa tài khoản / Suspend / Ban): Admin chuyển status sang SUSPENDED (tạm khóa) hoặc BANNED (khóa vĩnh viễn) -> Thu hồi Refresh Token trên DB, vô hiệu hóa Access Token, người dùng bị văng phiên và chặn mọi thao tác tiếp theo.
  - Nhánh 3B (Mở khóa tài khoản / Unban / Approve): Admin xem xét gỡ phạt, chuyển status về ACTIVE -> Phục hồi quyền đăng nhập và hoạt động bình thường của người dùng.

Bước 4: Xóa mềm tài khoản
- Người thực hiện: admin (Password123!)
- API Gateway: DELETE /api/v1/admin/user/{userId}
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Đánh dấu isDeleted = true, vô hiệu hóa vĩnh viễn quyền đăng nhập nhưng vẫn bảo tồn lịch sử hóa đơn chứng từ kế toán trong Database.

---

### FLOW 6: BẢO MẬT VÀ QUÊN MẬT KHẨU

Mục tiêu: Đảm bảo an toàn cho cơ chế phục hồi mật khẩu, ngăn chặn dò quét danh tính người dùng (Anti-User Enumeration) và chống brute-force.

Bước 1: Yêu cầu khôi phục mật khẩu
- Người thực hiện: Guest
- API Gateway: POST /api/v1/auth/password/forgot
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Cơ chế Anti-User Enumeration: Luôn trả về HTTP 200 dù email có tồn tại hay không nhằm ngăn chặn kẻ tấn công dò quét email người dùng.
  - Nếu email tồn tại: Sinh mã OTP lưu Redis (TTL 5 phút), gửi mail qua NotificationService:8007. Áp dụng cooldown 60 giây và tối đa 10 lần/ngày.

Bước 2: Đặt lại mật khẩu mới
- Người thực hiện: Guest
- API Gateway: PUT /api/v1/auth/password/reset
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Nhánh 2A (Chấp thuận đổi mật khẩu / Success): Mã OTP đúng, mật khẩu mới đạt chuẩn an toàn và không trùng mật khẩu cũ -> Mã hóa BCrypt lưu DB, xóa mã OTP trên Redis, thu hồi toàn bộ Refresh Token cũ trên tất cả các thiết bị.
  - Nhánh 2B (Từ chối đổi mật khẩu / Reject): Mã OTP sai, OTP quá 5 phút hết hạn, hoặc mật khẩu mới trùng với mật khẩu cũ -> Trả về HTTP 400 Bad Request.

Bước 3: Cơ chế phòng vệ Brute-force khi đăng nhập sai nhiều lần
- Người thực hiện: Kẻ tấn công / Người dùng nhập sai
- API Gateway: POST /api/v1/auth/login
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Sai từ lần 1 đến lần 4: Báo lỗi thông tin đăng nhập không hợp lệ kèm số lần còn lại.
  - Sai đến lần thứ 5 liên tiếp: Hệ thống tự động khóa tài khoản tạm thời trong 30 phút, trả về HTTP 423 Locked mà không cần kiểm tra mật khẩu trong DB.

Bước 4: Đăng xuất thu hồi phiên làm việc
- Người thực hiện: All roles (admin, staff, seller, buyer)
- API Gateway: POST /api/v1/auth/logout
- Service: UserService:8001
- Tình huống và Điều kiện thực tế:
  - Thu hồi Refresh Token trên DB và vô hiệu hóa Access Token để ngăn chặn việc sử dụng lại phiên đăng nhập cũ.

---

### FLOW 7: ĐẤU GIÁ SẢN PHẨM (AUCTION), ĐẶT CỌC VÍ, ĐẶT GIÁ REAL-TIME, CHỐNG BẮN TỈA VÀ QUYẾT TOÁN

Mục tiêu: Khép kín toàn bộ phân hệ Đấu giá từ lúc mở phiên, đặt cọc ví, trả giá trực tiếp, kích hoạt chống bắn tỉa đến khi quyết toán kết quả thắng/thua.

Bước 1: Seller tạo phiên đấu giá mới
- Người thực hiện: seller (Password123!)
- API Gateway: POST /api/v1/auctions
- Service: AuctionService:8005
- Tình huống và Điều kiện thực tế:
  - Nhánh 1A (Chấp thuận tạo phiên / Approve): Điểm uy tín Seller >= 50, sản phẩm có cờ đấu giá (isAuctionEnabled = true), thời lượng từ 60 phút đến 7 ngày, seller chưa vượt quá 5 phiên active -> Phiên chuyển sang trạng thái SCHEDULED hoặc ACTIVE khi đến giờ mở.
  - Nhánh 1B (Từ chối tạo phiên / Reject): Điểm uy tín Seller < 50, sản phẩm chưa bật tính năng đấu giá, hoặc seller đang có 5 phiên active -> Trả về HTTP 400 Bad Request.

Bước 2: Buyer nạp tiền vào ví điện tử
- Người thực hiện: buyer (Password123!)
- API Gateway: POST /api/v1/wallet/topup
- Service: WalletService:8008
- Tình huống và Điều kiện thực tế:
  - Buyer nạp tiền để chuẩn bị số dư khả dụng (availableBalance) tham gia đấu giá.

Bước 3: Đặt cọc tham gia phiên đấu giá
- Người thực hiện: buyer (Password123!)
- API Gateway: POST /api/v1/auctions/{auctionId}/deposit
- Service: AuctionService:8005
- Tình huống và Điều kiện thực tế:
  - Nhánh 3A (Chấp thuận đặt cọc / Approve): Số dư khả dụng trong ví >= tiền cọc yêu cầu -> WalletService:8008 đóng băng tiền cọc (availableBalance -= tiền_cọc, frozenBalance += tiền_cọc), cấp quyền đặt giá cho Buyer.
  - Nhánh 3B (Từ chối đặt cọc / Reject): Số dư khả dụng không đủ -> Trả về HTTP 400 Insufficient Balance, yêu cầu nạp thêm tiền.

Bước 4: Đặt giá cạnh tranh (Bidding)
- Người thực hiện: buyer (Password123!)
- API Gateway: POST /api/v1/auctions/{auctionId}/bids
- Service: AuctionService:8005
- Tình huống và Điều kiện thực tế:
  - Nhánh 4A (Chấp thuận đặt giá / Success):
    + Điều kiện: Phiên đang ACTIVE, điểm uy tín Buyer >= 40, và Mức giá đặt >= Giá cao nhất hiện tại + Bước giá (bidIncrement).
    + Xử lý nguyên tử (Atomic): Cập nhật giá cao nhất mới.
    + Cơ chế Chống bắn tỉa giây cuối (Anti-Sniping): Nếu có lượt đặt giá hợp lệ xuất hiện trong vòng 5 phút cuối trước khi hết giờ, hệ thống tự động gia hạn thêm 5 phút để bảo đảm công bằng.
    + Bắn sự kiện Outbid thông báo cho người vừa bị trả đè giá.
  - Nhánh 4B (Từ chối đặt giá / Reject): Mức giá đặt thấp hơn giá yêu cầu tối thiểu, hoặc điểm uy tín Buyer < 40 -> Trả về HTTP 400 Bad Request.

Bước 5: Quyết toán phiên đấu giá khi hết giờ (Auction Settlement)
- Người thực hiện: Hệ thống tự động khi hết giờ
- Service: AuctionService:8005
- Tình huống và Điều kiện thực tế:
  - Khi hết giờ, phiên chuyển sang trạng thái CLOSED, khóa toàn bộ lượt bid tiếp theo.
  - Nhánh 5A (Có người chiến thắng / AuctionWon):
    + Xác định duy nhất 1 người trả giá cao nhất là người thắng cuộc.
    + Bắn sự kiện AuctionWon sang CommerceService:8003 để tự động sinh đơn hàng chờ thanh toán trong vòng 24 giờ.
    + Kích hoạt WalletService:8008 giải tỏa tiền cọc (Unfreeze Balance) hoàn trả lại 100% cho tất cả những người tham gia bị thua cuộc.
  - Nhánh 5B (Phiên thất bại không có ai mua / AuctionFailed):
    + Hết giờ nhưng không có lượt đặt giá hợp lệ nào -> Đóng phiên FAILED, thông báo cho Seller và không sinh đơn hàng.
  - Nhánh 5C (Admin cưỡng chế hủy phiên / Admin Cancel Auction):
    + Phát hiện hành vi gian lận thông thầu -> Admin hủy phiên qua API: DELETE /api/v1/auctions/{id}/cancel. Hệ thống hoàn trả 100% tiền cọc cho tất cả người tham gia hợp lệ.

Bước 6: Người thắng thanh toán hoặc Bỏ cọc
- Người thực hiện: buyer trúng đấu giá
- API Gateway: POST /api/v1/orders/{orderId}/pay-auction
- Service: CommerceService:8003
- Tình huống và Điều kiện thực tế:
  - Nhánh 6A (Thanh toán thành công / Approve): Người thắng thanh toán trong vòng 24 giờ -> Tiền cọc được khấu trừ vào tổng tiền đơn hàng, đơn chuyển sang giao hàng GHN.
  - Nhánh 6B (Bỏ cọc / Bùng hàng / Reject): Quá 24 giờ không thanh toán -> Hệ thống tự động hủy kết quả thắng cuộc, tịch thu toàn bộ tiền cọc chuyển vào quỹ bồi thường cho Seller, và phạt trừ 10 điểm uy tín của Buyer (PAYMENT_TIMEOUT_PENALTY_SCORE = 10).

---

## 4. KỊCH BẢN DEMO

### Phần 1: Demo Luồng Người dùng & Nộp hồ sơ Shop 
- Bước 1: Đăng ký tài khoản mới -> Nhận mail OTP -> Nhập OTP xác thực -> WalletService tự động mở ví cá nhân 0 VNĐ. (BẮT ĐẦU CODE WALLET)
- Bước 2: Đăng nhập với tài khoản `buyer` (`Password123!`).

- Bước 3: Buyer gửi đơn xin mở gian hàng Seller với thông tin kho GHN. --> CÁI NÀY CẦN XEM XÉT LẠI !!
- Bước 4: Chuyển sang tài khoản `staff` (`Password123!`) -> Vào danh sách đơn chờ duyệt -> Xem chi tiết hồ sơ -> Bấm Phê duyệt.
- Bước 5: Tài khoản buyer trở thành seller, mở giao diện quản lý gian hàng.

--> XEM XÉT THỬ LUỒNG ĐĂNG KÍ THÀNH SELLER HOẶC BUYER TỪ ĐẦU

### Phần 2: Demo Luồng E-Commerce: Mua hàng, Giữ kho tạm, Thanh toán Webhook và GHN 
- Bước 1: Đăng nhập `seller` (`Password123!`) -> Đăng sản phẩm mới (số lượng tồn: 10) -> Xem số lượng tồn ban đầu trên CatalogService. (CHỈNH LẠI THÀNH FULFILLMENT)

- Bước 2: Đăng nhập `buyer` (`Password123!`) -> Xem chi tiết sản phẩm -> Bỏ vào giỏ hàng (chứng minh tồn kho vẫn giữ nguyên 10).

(PHẦN LỚN FULFILMENT)
- Bước 3: Buyer tiến hành Checkout -> Đơn hàng sang PENDING_PAYMENT -> Kiểm tra số lượng trên CatalogService (CHỈNH LẠI THÀNH FULFILLMENT):
  reservedStock tăng 1, availableStock giảm xuống 9, totalStock vẫn là 10.
- Bước 4: Giả lập gọi Webhook thanh toán `POST /api/v1/payments/simulate-webhook` -> Đơn hàng đổi sang PAID -> Kiểm tra số lượng trên CatalogService:
  totalStock giảm xuống 9, reservedStock trở về 0 -> Kiểm tra FulfillmentService đã tự động sinh mã vận đơn GHN.

### Phần 3: Demo Luồng Đấu giá (Auction) & Đặt cọc Ví điện tử 
- Bước 1: Đăng nhập `seller` (`Password123!`) -> Mở phiên đấu giá cho sản phẩm, thiết lập giá sàn, bước giá, tiền cọc.
- Bước 2: Đăng nhập `buyer` (`Password123!`) -> Nạp tiền vào ví cá nhân -> Đặt cọc tham gia phiên (chứng minh tiền bị đóng băng trên WalletService).
- Bước 3: Thực hiện đặt giá cạnh tranh (bids) -> Thể hiện cơ chế kiểm tra bước giá, cập nhật giá cao nhất tức thì và chống bắn tỉa (tự động gia hạn 5 phút nếu bid ở phút chót).
- Bước 4: Đóng phiên đấu giá -> Hệ thống chốt người chiến thắng duy nhất, tự động sinh đơn hàng sang CommerceService, đồng thời mở cọc (unfreeze) cho những người thua cuộc.

### Phần 4: Demo Luồng Admin, Quản trị tài khoản & Khóa vi phạm
- Bước 1: Đăng nhập `admin` (`Password123!`) -> Tra cứu danh sách người dùng theo vai trò và trạng thái.
- Bước 2: Admin thực hiện Khóa tài khoản vi phạm -> Chuyển sang màn hình của người dùng bị khóa, gọi request kế tiếp và chứng minh người dùng bị chặn ngay lập tức, văng khỏi hệ thống.
- Bước 3: Thử đăng nhập sai mật khẩu 5 lần liên tiếp -> Chứng minh hệ thống tự động khóa tài khoản tạm thời 30 phút (HTTP 423 Locked).
