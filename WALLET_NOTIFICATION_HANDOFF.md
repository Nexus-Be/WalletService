# Bàn giao tích hợp Notification cho WalletService

Hai luồng cần Wallet xác nhận hợp đồng là **kết quả rút tiền** và **thay đổi tiền cọc đấu giá**. Các endpoint bên dưới đang được đề xuất.

| Luồng | Route dự kiến | Dữ liệu cần chốt |
|---|---|---|
| Rút tiền | `POST /api/v1/notifications/withdrawal-events` | ID yêu cầu, người nhận, trạng thái `Approved`, `Rejected`, `Completed` hoặc `Failed`, thời điểm xác nhận, lý do được phép hiển thị và mã giao dịch nếu phù hợp. |
| Tiền cọc đấu giá | `POST /api/v1/notifications/auction-deposits` | ID giao dịch, phiên, bidder, trạng thái giữ/hoàn/tịch thu, số tiền, đơn vị tiền và thời điểm xác nhận. Chốt một bên phát mỗi trạng thái với Auction. |

Với hoàn tiền đơn hàng và quyết toán, đề nghị Wallet cung cấp kết quả tài chính đã xác nhận cho service sở hữu đơn. Notification có sẵn `order-refunds` và `order-settlements`. Thống nhất bên phát để một giao dịch không tạo hai thông báo.

- [ ] Xác nhận bên phát và trạng thái tài chính dùng làm nguồn sự thật.
- [ ] Gửi payload mẫu và chốt quyền S2S trước khi wiring.
- [ ] Nghiệm thu đúng người nhận, đúng kết quả đã xác nhận và không trùng khi replay.

## Thống nhất khi bàn giao

- Phát sự kiện qua REST sau khi lưu nghiệp vụ thành công. Với API có `sourceEventId`, dùng ID ổn định và giữ nguyên payload khi thử lại. Với API có `traceId`, dùng để đối soát. Lỗi gửi thông báo cần được ghi nhận để xử lý lại mà không thay đổi kết quả nghiệp vụ đã lưu.
- Chạy ít nhất ca thành công, ca thất bại nghiệp vụ và ca phát lại áp dụng được. Ghi ID nghiệp vụ, ID sự kiện, HTTP status và ID thông báo. Đối chiếu inbox hoặc hộp thư của người nhận theo từng luồng.
- Phản hồi bằng nhánh hoặc PR chứa caller, điểm phát sự kiện, ví dụ payload và kết quả kiểm thử. Phía Notification tham chiếu bản `dev` commit `a767334`. Các thay đổi Gateway/FE cần được đồng bộ khi nghiệm thu.

