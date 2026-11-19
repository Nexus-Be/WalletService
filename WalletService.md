# BÁO CÁO TỔNG HỢP: WalletService

> Tài liệu gộp đầy đủ **3 phần** cho thành viên phụ trách **WalletService**:
> - **Phần I:** Toàn bộ thay đổi code hiện có trên local (BE + 
exus-dev-be) & lý do thay đổi
> - **Phần II:** Đối soát toàn bộ API hiện có (lỗi logic/auth/route/S2S) & danh sách API còn thiếu (gồm GHN Master Data & Địa chỉ mặc định / Đổi địa chỉ)
> - **Phần III:** Kế hoạch triển khai & fix chi tiết từng bước

---

# PHẦN I: NHỮNG THAY ĐỔI CODE HIỆN CÓ & LÝ DO

# Báo cáo Thay đổi Mã nguồn — `WalletService`

## 1. Trạng thái Git & Tổng quan (`BE\WalletService`)

- **Nhánh hiện tại:** `feature/stripe-topup-resume-and-reconciliation` (**KHÔNG** ở nhánh `dev`)
- **So với `origin/dev`:** Behind `origin/dev`: **2** | Ahead of `origin/dev`: **2** (**CHƯA** pull/merge 2 commit mới trên `origin/dev`):
  - `f55185e fix(docker): correct environment variable names for Stripe integration`
  - `246594a feat(wallet): Enhance wallet service with new financial operations and reporting features`
- **Tổng số file thay đổi/thêm/xóa so với `HEAD` (trong `C:\Users\ACER\Desktop\BE\WalletService`):** 55 file (37 `Modified`, 1 `Deleted`, 17 `Added` / untracked), không tính `_changes.diff`.
  - *Lưu ý:* File `IWalletDbContext.cs`, `WalletDbContext.cs` và `TestWalletDbContext.cs` có thay đổi tạm ở staged (`WalletHolds`, `LockHoldReferenceAsync`) nhưng đã được hoàn tác ở working tree nên net diff so với `HEAD` là rỗng.

### Tóm tắt các nhóm thay đổi chính trong `BE\WalletService`
1. **Quy trình rút tiền (Withdrawal Workflow & Idempotency):** Mở rộng vòng đời rút tiền từ 2 hành động (`Approve`/`Reject`) lên 6 hành động (`Approve`, `Reject`, `StartProcessing`, `Complete`, `Fail`, `ResolveFailed`), tách bạch bước trừ tiền (`Approve`), xác nhận chuyển khoản (`Complete` với `GatewayReferenceId` duy nhất), hoàn tiền (`Fail`/`ResolveFailed`), kiểm tra quyền Admin riêng cho `ResolveFailed`, bổ sung `Idempotency-Key` + `RequestFingerprint` (SHA-256) khi tạo yêu cầu rút tiền, và thêm các API truy vấn lịch sử/danh sách rút tiền cho seller và staff/admin.
2. **Đóng băng / Mở đóng băng ví & API Nội bộ (Wallet Freeze/Unfreeze & Internal API):** Thêm `WalletAccount.Freeze()`/`Unfreeze()`, command `FreezeWalletCommand`/`UnfreezeWalletCommand`, endpoint admin (`GET {userId}`, `POST {userId}/freeze`, `POST {userId}/unfreeze` trên `AdminWalletsController`) và endpoint internal (`POST /api/v1/internal/wallets/freeze-purchase`, `unfreeze-purchase` trên `InternalWalletsController`) bảo vệ bằng `RequireInternalServiceKeyAttribute` (`X-Internal-Service-Key`).
3. **Stripe Top-up Resume, History & Validation:** Tập trung logic liệt kê và resume top-up vào `StripeTopupHistory` (chỉ trả lại `CheckoutUrl` khi session còn mở, không gọi Stripe API để tạo/đọc session khi resume, tránh cộng tiền trùng), thêm bộ lọc `status` cho `GET /api/v1/wallets/topups`, chuẩn hóa validation cấu hình Stripe khi khởi động (`StripeSettings`), và gỡ bỏ background worker `StripeTopupReconciliationWorker`.
4. **Auto-create ví, Auction Holds & Thanh toán/Tất toán đơn hàng:** Bổ sung `WalletAccountExtensions.LockOrEnsureWalletByUserIdAsync` để tự động tạo ví VND mặc định nếu chưa có khi freeze/unfreeze, settle seller order, top-up hoặc direct Stripe checkout; sửa kiểm tra số dư khả dụng (`AvailableBalance`) trong `PayOrderWithWalletCommandHandler`; bổ sung fallback key cho `RequireAuctionServiceKeyAttribute`, cập nhật `Status` khi replay `CreateHoldCommandHandler`, thêm lý do `BUYER_WITHDRAWN` cho `ReleaseHoldCommand`, và thêm ràng buộc DB `CK_Wallets_BalanceLocks`.
5. **Migration & Kiểm thử (Unit Tests):** Thêm migration `20261117100000_AddWithdrawalWorkflowFields`, cập nhật 8 file test cũ và thêm 4 file test mới bao phủ các luồng trên.

---

## 2. Chi tiết Thay đổi theo Từng File (`BE\WalletService`)

### A. Cấu hình & Khởi động (`Configuration` & `Wallet.Api`)

#### 1. `.env.example` — `Modified`
- **Thay đổi:** Cập nhật chú thích cho `STRIPE_SUCCESS_URL` và `STRIPE_CANCEL_URL`: yêu cầu HTTPS, chỉ cho phép HTTP thuần với `localhost` khi `ASPNETCORE_ENVIRONMENT=Development`, ghi rõ Wallet tự nối `?transactionId=<top-up id>` và service sẽ từ chối khởi động nếu cấu hình sai.
- **Lý do:** Đồng bộ tài liệu cấu hình với logic kiểm tra trong `StripeSettings.EnsureValid`.

#### 2. `env` — `Added` (untracked)
- **Thay đổi:** File biến môi trường Docker Compose local (được Git ignore trong repo gốc), chứa `ASPNETCORE_ENVIRONMENT=Development`, `API_GATEWAY_PORT=8000`, `WALLET_SERVICE_PORT=8008`, `CONNECTION_STRING`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `OTEL_EXPORTER_OTLP_ENDPOINT`.
- **Lý do:** Phục vụ chạy local Docker Compose / kết nối DB dev.

#### 3. `Wallet.Api/Program.cs` — `Modified`
- **Thay đổi:**
  - Xóa `using Wallet.Api;` và xóa đăng ký `builder.Services.AddHostedService<StripeTopupReconciliationWorker>();`.
  - Thêm `Wallet.Infrastructure.Payments.StripeSettings.EnsureValid(app.Configuration, app.Logger);` ngay sau `builder.Build()`.
- **Lý do:** Fail-fast khi khởi động nếu cấu hình Stripe không hợp lệ (tránh lỗi 500 ở runtime khi tạo top-up/webhook) và ngừng chạy background worker đối soát định kỳ cũ.

#### 4. `Wallet.Api/StripeTopupReconciliationWorker.cs` — `Deleted`
- **Thay đổi:** Xóa class `StripeTopupReconciliationWorker : BackgroundService` (trước đây dùng `PeriodicTimer` mỗi 1 phút gửi `ReconcilePendingTopupsCommand`).
- **Lý do:** Gỡ bỏ worker đối soát định kỳ tự động trong API host.

#### 5. `Wallet.Api/Controllers/Public/AdminWalletsController.cs` — `Modified` (staged + unstaged sau 11:07 AM)
- **Thay đổi:**
  - Thêm `using` cho `FreezeWallet`, `UnfreezeWallet`, `GetMyWallet`, `Withdrawals.Queries`.
  - Trong `ProcessWithdrawal`: truyền thêm `ActorIsAdmin = this.IsAdmin()` vào `ProcessWithdrawalCommand` để handler kiểm tra quyền admin cho `ResolveFailed`.
  - Thêm 2 endpoint rút tiền cho staff/admin:
    - `GET api/v1/admin/wallets/withdrawals` (`ListWithdrawals(page=1, pageSize=20, status=null)`): gọi `ListAdminWithdrawalsQuery`.
    - `GET api/v1/admin/wallets/withdrawals/{id:guid}/history` (`GetWithdrawalHistory(id)`): gọi `GetWithdrawalHistoryQuery`.
  - *(Cập nhật sau 11:07 AM — unstaged)* Thêm 3 endpoint quản lý ví user + record request:
    - `GET api/v1/admin/wallets/{userId:guid}` (`GetUserWallet(Guid userId)`): gọi `GetMyWalletQuery(userId.ToString())` để admin/staff xem chi tiết ví của user.
    - `POST api/v1/admin/wallets/{userId:guid}/freeze` (`FreezeWallet(Guid userId, [FromBody] AdminWalletFreezeRequest? request)`): kiểm tra `TryGetUserId`, log `userId`, `adminUserId`, `Reason` và gửi `FreezeWalletCommand(userId, request?.Reason)`.
    - `POST api/v1/admin/wallets/{userId:guid}/unfreeze` (`UnfreezeWallet(Guid userId, [FromBody] AdminWalletFreezeRequest? request)`): kiểm tra `TryGetUserId`, log `userId`, `adminUserId`, `Reason` và gửi `UnfreezeWalletCommand(userId, request?.Reason)`.
    - Thêm `public sealed record AdminWalletFreezeRequest(string? Reason);`.
- **Lý do:** Hỗ trợ staff/admin xem hàng đợi yêu cầu rút tiền, xem lịch sử audit từng bước, phân quyền chặt cho `ResolveFailed`, đồng thời cho phép admin/staff tra cứu và đóng băng/mở đóng băng ví user.

#### 6. `Wallet.Api/Controllers/Public/WalletsController.cs` — `Modified`
- **Thay đổi:**
  - `POST api/v1/wallets/withdrawals` (`Withdraw`): nhận thêm header `[FromHeader(Name = "Idempotency-Key")] string? idempotencyKey`, truyền `IdempotencyKey = idempotencyKey?.Trim() ?? string.Empty` vào `RequestWithdrawalCommand`; trả về `200 OK` nếu `result.IsReplay` là `true`, ngược lại trả về `201 Created`.
  - Thêm `GET api/v1/wallets/withdrawals` (`ListWithdrawals`, `[RequireRole("seller")]`): gọi `ListMyWithdrawalsQuery`.
  - Thêm `GET api/v1/wallets/withdrawals/{id:guid}` (`GetWithdrawal`, `[RequireRole("seller")]`): gọi `GetMyWithdrawalQuery`.
  - `GET api/v1/wallets/topups` (`ListTopups`): đổi kiểu response sang `PagedData<TopupListItemResponse>` và thêm query parameter `string? status = null`.
  - `POST api/v1/wallets/topups/{id:guid}/resume` (`ResumeTopup`): bổ sung `[ProducesResponseType(StatusCodes.Status404NotFound)]` và `[ProducesResponseType(StatusCodes.Status409Conflict)]`.
- **Lý do:** Đảm bảo idempotency khi seller tạo lệnh rút tiền (tránh khóa tiền hai lần khi retry), cho phép seller theo dõi danh sách/chi tiết lệnh rút tiền và lọc danh sách top-up theo trạng thái.

#### 7. `Wallet.Api/Controllers/Internal/InternalWalletsController.cs` — `Added` (untracked)
- **Chi tiết:** Controller `[Route("api/v1/internal/wallets")]` gắn `[RequireInternalServiceKey]`, định nghĩa `WalletFreezeRequest(Guid UserId, string? Reason)` và 2 endpoint:
  - `POST freeze-purchase` (`FreezePurchase`): gửi `FreezeWalletCommand(request.UserId, request.Reason)` để đóng băng ví khi phát sinh tranh chấp.
  - `POST unfreeze-purchase` (`UnfreezePurchase`): gửi `UnfreezeWalletCommand(request.UserId, request.Reason)` để mở đóng băng ví sau khi giải quyết tranh chấp.
- **Lý do:** Cung cấp API nội bộ cho các microservice khác (như Order/Dispute) đóng băng/mở đóng băng ví khi xử lý tranh chấp.

#### 8. `Wallet.Api/Filters/RequireInternalServiceKeyAttribute.cs` — `Added` (untracked)
- **Chi tiết:** Filter `IAuthorizationFilter` kiểm tra header `X-Internal-Service-Key` bằng `CryptographicOperations.FixedTimeEquals` trên SHA-256 hash so với `INTERNAL_SERVICE_SECRET` (env/config) hoặc `InternalSecurity:ServiceKey`. Trả về `500 INTERNAL_CONFIGURATION_ERROR` nếu chưa cấu hình và `401 UNAUTHORIZED` nếu sai/thiếu header.
- **Lý do:** Bảo vệ các internal endpoint khỏi truy cập trái phép và chống timing attack.

#### 9. `Wallet.Api/Filters/RequireAuctionServiceKeyAttribute.cs` — `Modified` (unstaged)
- **Thay đổi:** Lấy khóa cấu hình theo thứ tự fallback `configuration["InternalSecurity:AuctionServiceKey"] ?? configuration["InternalSecurity:ServiceKey"]`.
- **Lý do:** Cho phép dùng chung `InternalSecurity:ServiceKey` nếu chưa cấu hình riêng `AuctionServiceKey`.

---

### B. Domain Layer (`Wallet.Domain`)

#### 10. `Wallet.Domain/Entities/WalletAccount.cs` — `Modified` (unstaged)
- **Thay đổi:** Thêm 2 method domain:
  - `Freeze()`: đặt `Status = "FROZEN"`, cập nhật `UpdatedAt = DateTimeOffset.UtcNow`.
  - `Unfreeze()`: đặt `Status = "ACTIVE"`, cập nhật `UpdatedAt = DateTimeOffset.UtcNow`.
- **Lý do:** Đóng gói thay đổi trạng thái đóng băng/mở đóng băng trên entity `WalletAccount`.

#### 11. `Wallet.Domain/Entities/WithdrawalRequest.cs` — `Modified`
- **Thay đổi:** Thêm 4 property mới:
  - `DateTimeOffset? CompletedAt` — thời điểm staff xác nhận chuyển khoản hoàn tất.
  - `string? StatusReason` — lý do từ chối hoặc thất bại (hiển thị cho seller, không chứa evidence nội bộ).
  - `string? ClientIdempotencyKey` — `Idempotency-Key` của seller (unique theo ví).
  - `string? RequestFingerprint` — mã SHA-256 của nội dung yêu cầu rút tiền.
- **Lý do:** Lưu trữ dữ liệu phục vụ quy trình rút tiền nhiều bước và chống tạo trùng yêu cầu.

#### 12. `Wallet.Domain/Enums/AuditAction.cs` — `Modified` (staged + unstaged)
- **Thay đổi:**
  - Nối thêm các giá trị enum mới vào cuối (giữ nguyên giá trị số của các enum cũ): `WithdrawalStartProcessing = 16`, `WithdrawalFail = 17`, `WithdrawalResolveFailed = 18`, `WalletFrozen = 19`, `WalletUnfrozen = 20`.
  - *(Unstaged)* Xóa các alias trùng giá trị số (`AuctionHoldCreated = 12`, `AuctionHoldReleased = 13`, `AuctionHoldCaptured = 14`, `AuctionHoldRefunded = 15`), giữ tên chuẩn `AuctionHoldCreate`..`AuctionHoldRefund`.
- **Lý do:** Ghi nhận audit log cho các trạng thái mới của withdrawal và thao tác đóng băng/mở đóng băng ví mà không làm lệch dữ liệu audit cũ trong DB.

---

### C. Application Layer (`Wallet.Application`)

#### 13. `Wallet.Application/Common/Extensions/WalletAccountExtensions.cs` — `Added` (untracked)
- **Chi tiết:** Class `internal static class WalletAccountExtensions` gồm:
  - `StageDefaultWallet(this IWalletDbContext context, Guid userId)`: tạo `WalletAccount` mới (`Currency = Vnd`, `Status = "ACTIVE"`, `Balance = 0`, `LockedBalance = 0`) và ghi `AuditAction.WalletCreate`.
  - `LockOrEnsureWalletByUserIdAsync(this IWalletDbContext context, Guid userId, CancellationToken)`: lock ví theo `userId` trong transaction hiện tại; nếu chưa có thì gọi `StageDefaultWallet` và `SaveChangesAsync`.
- **Lý do:** Tránh lỗi không tìm thấy ví khi thực hiện freeze/unfreeze, settle seller order, top-up hoặc direct Stripe checkout cho user chưa từng mở ví.

#### 14. `Wallet.Application/Features/Wallets/Commands/EnsureWallet/EnsureWalletCommandHandler.cs` — `Modified` (unstaged)
- **Thay đổi:** Thay đoạn khởi tạo `WalletAccount` + `AddAuditLog` trực tiếp bằng `_context.StageDefaultWallet(request.UserId)`.
- **Lý do:** Tái sử dụng helper chuẩn trong `WalletAccountExtensions`, tránh trùng lặp logic tạo ví mặc định.

#### 15. `Wallet.Application/Features/Wallets/Commands/FreezeWallet/FreezeWalletCommand.cs` — `Added` (untracked)
- **Chi tiết:** Định nghĩa `FreezeWalletCommand(Guid UserId, string? Reason) : IRequest<WalletFreezeStatusResponse>` và `WalletFreezeStatusResponse(Guid WalletId, Guid UserId, string Status, string Reason, DateTimeOffset UpdatedAt)`.

#### 16. `Wallet.Application/Features/Wallets/Commands/FreezeWallet/FreezeWalletCommandHandler.cs` — `Added` (untracked)
- **Chi tiết:** Chứa `WalletFreezeHelper.UpdateFreezeStateAsync` và `FreezeWalletCommandHandler`: trong transaction, gọi `LockOrEnsureWalletByUserIdAsync`, thực hiện `wallet.Freeze()`/`Unfreeze()` nếu trạng thái hiện tại khác trạng thái đích (idempotent nếu đã ở trạng thái đích, không ghi trùng audit log), ghi audit `AuditAction.WalletFrozen`/`WalletUnfrozen`.
- **Lý do:** Hỗ trợ đóng băng ví phục vụ điều tra tranh chấp hoặc thao tác admin (kể cả khi user chưa có ví trước đó).

#### 17. `Wallet.Application/Features/Wallets/Commands/UnfreezeWallet/UnfreezeWalletCommand.cs` — `Added` (untracked)
- **Chi tiết:** Định nghĩa `UnfreezeWalletCommand(Guid UserId, string? Reason) : IRequest<WalletFreezeStatusResponse>` và `UnfreezeWalletCommandHandler` ủy quyền cho `WalletFreezeHelper.UpdateFreezeStateAsync(..., freeze: false, defaultReason: "DISPUTE_RESOLVED", ...)`.

#### 18. `Wallet.Application/Features/Withdrawals/WithdrawalWorkflow.cs` — `Added` (untracked)
- **Chi tiết:** Định nghĩa `enum WithdrawalAction` (`Approve`, `Reject`, `StartProcessing`, `Complete`, `Fail`, `ResolveFailed`) và `static class WithdrawalWorkflow`:
  - Hằng số: `MaxReasonLength = 500`, `MaxReferenceLength = 100`, `MinReasonLength = 3`.
  - Bảng chuyển trạng thái `Transitions`: `Approve` (`Pending -> Approved`), `Reject` (`Pending -> Rejected`), `StartProcessing` (`Approved -> Processing`), `Complete` (`Approved | Processing -> Completed`), `Fail` (`Approved -> Failed`), `ResolveFailed` (`Processing -> Failed`, chỉ Admin qua `RequiresAdmin`).
  - Các helper: `TryParse`, `CanTransition`, `TargetOf`, `RequiresAdmin`, `ReversesTheDebit`, `AuditActionOf`, `HistoryNameOf`, `Fingerprint` (SHA-256), `MaskAccount`, `IsValidIdempotencyKey` (1–100 ký tự ASCII chữ/số/`-`/`_`).
- **Lý do:** Tập trung toàn bộ state machine và quy tắc nghiệp vụ rút tiền vào một nguồn duy nhất.

#### 19. `Wallet.Application/Features/Withdrawals/WithdrawalReader.cs` — `Added` (untracked)
- **Chi tiết:** Định nghĩa `WithdrawalSummaryResponse` (che số tài khoản bằng `MaskAccount`, không lộ `ProcessedBy` hay `VerificationEvidence`), `AdminWithdrawalResponse` (đầy đủ thông tin ngân hàng + `OwnerUserId` + `TransferReference`), `WithdrawalHistoryEntry` và các hàm đọc `ListMineAsync`, `GetMineAsync`, `ListAdminAsync`, `HistoryAsync`.
- **Lý do:** Cung cấp API đọc danh sách/chi tiết/lịch sử rút tiền an toàn theo vai trò (seller vs staff/admin).

#### 20. `Wallet.Application/Features/Withdrawals/Queries/WithdrawalQueries.cs` — `Added` (untracked)
- **Chi tiết:** Định nghĩa 4 MediatR query + validation + handler: `ListMyWithdrawalsQuery`, `GetMyWithdrawalQuery`, `ListAdminWithdrawalsQuery`, `GetWithdrawalHistoryQuery`.
- **Lý do:** Tách biệt luồng truy vấn CQRS cho các endpoint rút tiền mới.

#### 21. `Wallet.Application/Features/Withdrawals/Commands/RequestWithdrawal/RequestWithdrawalCommand.cs` — `Modified`
- **Thay đổi:** Thêm `[JsonIgnore] public string IdempotencyKey { get; init; } = string.Empty;`, validate `IdempotencyKey` qua `WithdrawalWorkflow.IsValidIdempotencyKey`, và thêm `[JsonIgnore] public bool IsReplay { get; init; }` vào `WithdrawalResponse`.
- **Lý do:** Ràng buộc đầu vào `Idempotency-Key` và báo hiệu replay cho controller trả HTTP 200.

#### 22. `Wallet.Application/Features/Withdrawals/Commands/RequestWithdrawal/RequestWithdrawalCommandHandler.cs` — `Modified`
- **Thay đổi:** Tính `fingerprint = WithdrawalWorkflow.Fingerprint(...)`; sau khi lock ví, kiểm tra `(wallet.Id, request.IdempotencyKey)` trong `WithdrawalRequests`: ném 409 `IDEMPOTENCY_KEY_REUSED` nếu khác fingerprint, trả `IsReplay = true` nếu trùng, hoặc lưu `ClientIdempotencyKey`/`RequestFingerprint` mới.
- **Lý do:** Đảm bảo an toàn concurrency và idempotency khi seller retry tạo yêu cầu rút tiền.

#### 23. `Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/ProcessWithdrawalCommand.cs` — `Modified`
- **Thay đổi:** Mở rộng regex `Action` thành `Approve|Reject|StartProcessing|Complete|Fail|ResolveFailed`; thay `ReferenceCode` bằng `TransferConfirmed`, `TransferReference` (max 100), `BankVerifiedNotTransferred`, `VerificationEvidence` (max 500), `ActorIsAdmin` (`[JsonIgnore]`), `AdminNote` (max 500); cập nhật `ValidateRequest()` và `ProcessWithdrawalResponse` (`TransferReference`, `StatusReason`).
- **Lý do:** Phản ánh đúng hợp đồng dữ liệu cho 6 bước xử lý rút tiền.

#### 24. `Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/ProcessWithdrawalCommandHandler.cs` — `Modified`
- **Thay đổi:** Kiểm tra action qua `WithdrawalWorkflow.TryParse` và quyền Admin cho `ResolveFailed` (403 `FORBIDDEN`); lock `WithdrawalRequest` và kiểm tra `WithdrawalWorkflow.CanTransition` (409 `WITHDRAWAL_INVALID_TRANSITION`); xử lý `Approve` (`CompleteLockedWithdrawal` + `WithdrawalDebit`), `Reject` (`ReleaseLockedFunds` + `StatusReason`), `StartProcessing`, `Complete` (kiểm tra trùng `GatewayReferenceId` 409 `TRANSFER_REFERENCE_ALREADY_USED` + `CompletedAt`), `Fail`/`ResolveFailed` (`wallet.Deposit` + `WithdrawalReversal` + `StatusReason`); ghi audit log theo `WithdrawalWorkflow.AuditActionOf(action)`.
- **Lý do:** Tách biệt thời điểm trừ tiền, xác nhận chuyển khoản và hoàn tiền trong một transaction an toàn.

#### 25. `Wallet.Application/Features/Checkout/Commands/PayOrderWithWallet/PayOrderWithWalletCommandHandler.cs` — `Modified`
- **Thay đổi:** Sửa điều kiện kiểm tra số dư từ `wallet.Balance < request.Amount` thành `wallet.AvailableBalance < request.Amount` (và truyền `wallet.AvailableBalance` vào `InsufficientBalanceException`).
- **Lý do:** Sửa lỗi cho phép chi tiêu cả phần tiền đang bị khóa (`LockedBalance`).

#### 26. `Wallet.Application/Features/Checkout/Commands/SettleSellerOrder/SettleSellerOrderCommandHandler.cs` — `Modified` (unstaged)
- **Thay đổi:** Thay `LockWalletByUserIdAsync(sellerId)` (+ ném `WalletNotFoundException`) bằng `LockOrEnsureWalletByUserIdAsync(sellerId, cancellationToken)`.
- **Lý do:** Tự động tạo ví VND mặc định nếu seller chưa mở ví trước khi tất toán đơn hàng.

#### 27. `Wallet.Application/Features/Holds/Commands/CreateHold/CreateHoldCommandHandler.cs` — `Modified` (unstaged)
- **Thay đổi:** Khi replay thao tác `create` từ `AuctionHoldOperations`, cập nhật `with { Status = existing.Status.ToString() }` trên `CreateHoldResponse` deserialize từ `original.ResponseJson`.
- **Lý do:** Trả về trạng thái hiện tại thực tế của hold (ví dụ đã `Released`/`Captured`) thay vì trạng thái cũ tại thời điểm tạo.

#### 28. `Wallet.Application/Features/Holds/Commands/ReleaseHold/ReleaseHoldCommand.cs` — `Modified` (unstaged)
- **Thay đổi:** Bổ sung `"BUYER_WITHDRAWN"` vào danh sách `Reason` hợp lệ trong `ValidateRequest()`.
- **Lý do:** Hỗ trợ nhả tiền cọc khi người mua rút lui theo quy trình đấu giá.

#### 29. `Wallet.Application/Features/Stripe/StripeDirectPreparation.cs` — `Modified` (unstaged)
- **Thay đổi:** Thay `LockWalletByUserIdAsync(ownerId)` (+ ném `WalletNotFoundException`) bằng `LockOrEnsureWalletByUserIdAsync(ownerId, token)`.
- **Lý do:** Hỗ trợ tự động tạo ví VND mặc định cho luồng thanh toán đơn hàng trực tiếp qua Stripe.

#### 30. `Wallet.Application/Features/Stripe/StripeTopupPreparation.cs` — `Modified` (staged + unstaged)
- **Thay đổi:**
  - *(Staged)* Truyền `topup.Id.ToString()` (chuỗi) thay vì `topup.Id` (`Guid`) cho tham số `ReturnReference` của `StripeCheckoutRequest`.
  - *(Unstaged)* Thay `LockWalletByUserIdAsync(ownerId)` (+ ném `WalletNotFoundException`) bằng `LockOrEnsureWalletByUserIdAsync(ownerId, token)`.
- **Lý do:** Khớp với chữ ký mới của `StripeCheckoutRequest` và tự động tạo ví nếu user chưa có ví khi nạp tiền.

#### 31. `Wallet.Application/Features/Stripe/StripeTopupHistory.cs` — `Added` (untracked)
- **Chi tiết:** Định nghĩa `enum TopupResumability` (`Resumable`, `PaymentInProgress`, `ReconciliationPending`, `NotResumable`), `StripeTopupListItem`, `StripeResumedTopup`, và `StripeTopupHistory`:
  - `Evaluate`: chỉ `Resumable` khi `topup.Status == Pending`, `attempt.Status == AwaitingPayment`, có `CheckoutUrl` và chưa hết hạn.
  - `ListAsync`: truy vấn danh sách top-up Stripe (`VnpTxnRef == null`) của user, hỗ trợ lọc theo `TopupStatus?`, phân trang, gắn cờ `CanResume` và `SessionExpiresAt` theo attempt mới nhất.
  - `ResumeAsync`: trả về `PaymentUrl` hiện có của top-up đang mở mà không gọi Stripe tạo session mới; trả lỗi 404 hoặc 409 tương ứng.
- **Lý do:** Tập trung hóa logic lịch sử và resume top-up, ngăn việc tạo session thừa hoặc cộng tiền sai khi mở lại link thanh toán.

#### 32. `Wallet.Application/Features/Topups/Commands/ResumeTopup/ResumeTopupCommand.cs` — `Modified`
- **Thay đổi:** Đổi tên tham số từ `TransactionId` thành `TopupId` với thông báo lỗi `"Topup ID must be a valid UUID."`.

#### 33. `Wallet.Application/Features/Topups/Commands/ResumeTopup/ResumeTopupCommandHandler.cs` — `Modified`
- **Thay đổi:** Loại bỏ `IStripeCheckoutClient` khỏi constructor và ủy quyền hoàn toàn cho `StripeTopupHistory.ResumeAsync`.

#### 34. `Wallet.Application/Features/Topups/Queries/GetTopupStatus/GetTopupStatusQuery.cs` — `Modified`
- **Thay đổi:** Xóa 2 property `CanResume` và `SessionExpiresAt` khỏi `TopupStatusResponse`.

#### 35. `Wallet.Application/Features/Topups/Queries/GetTopupStatus/GetTopupStatusQueryHandler.cs` — `Modified`
- **Thay đổi:** Xóa truy vấn `StripePaymentAttempts` và tính toán `canResume`, chỉ gọi `StripeTopupStatus.GetAsync`.

#### 36. `Wallet.Application/Features/Topups/Queries/ListTopups/ListTopupsQuery.cs` — `Modified`
- **Thay đổi:** Thêm tham số `string? Status = null` + validate `TopupStatus`; đổi tên `TopupListItem` thành `TopupListItemResponse`.

#### 37. `Wallet.Application/Features/Topups/Queries/ListTopups/ListTopupsQueryHandler.cs` — `Modified`
- **Thay đổi:** Thay truy vấn EF trực tiếp bằng `PaginationConstants.Normalize` + `StripeTopupHistory.ListAsync`.

#### 38. `Wallet.Application/Interfaces/IStripeCheckoutClient.cs` — `Modified`
- **Thay đổi:** Đổi tham số thứ 4 của `StripeCheckoutRequest` từ `Guid? TopupId = null` thành `string? ReturnReference = null`.

---

### D. Infrastructure Layer (`Wallet.Infrastructure`)

#### 39. `Wallet.Infrastructure/Payments/StripeSettings.cs` — `Added` (untracked)
- **Chi tiết:** Class `public static class StripeSettings`:
  - `ReturnReferenceParameter = "transactionId"`.
  - `IsDevelopment`, `RequireReturnUrl` (bắt buộc `https://`, chỉ cho phép `http://` với `localhost`/`127.0.0.1`/`::1` ở `Development`), `WithReturnReference` (gắn/ghi đè query parameter `transactionId`).
  - `Validate` và `EnsureValid`: kiểm tra `Stripe:SecretKey` (`sk_test_...`), `Stripe:WebhookSecret`, `Stripe:SuccessUrl`, `Stripe:CancelUrl`; log warning nếu chưa cấu hình Stripe, ném `InvalidOperationException` nếu cấu hình dở dang/sai.
- **Lý do:** Dùng chung quy tắc giữa `StripeCheckoutClient` và bước kiểm tra lúc khởi động trong `Program.cs`.

#### 40. `Wallet.Infrastructure/Payments/StripeCheckoutClient.cs` — `Modified`
- **Thay đổi:** Thay logic nội bộ kiểm tra URL và gắn context bằng `StripeSettings.IsDevelopment`, `StripeSettings.RequireReturnUrl` và `StripeSettings.WithReturnReference`.

#### 41. `Wallet.Infrastructure/Persistence/Configurations/WalletAccountConfiguration.cs` — `Modified`
- **Thay đổi:** Thêm check constraint `CK_Wallets_BalanceLocks` (`"Balance" >= 0 AND "LockedBalance" >= 0 AND "LockedBalance" <= "Balance"`).

#### 42. `Wallet.Infrastructure/Persistence/Configurations/WithdrawalRequestConfiguration.cs` — `Modified`
- **Thay đổi:** Cấu hình độ dài tối đa (`StatusReason` 500, `ClientIdempotencyKey` 100, `RequestFingerprint` 64) và thêm 3 index: `IX_WithdrawalRequests_GatewayReferenceId` (Unique filtered), `IX_WithdrawalRequests_WalletId_ClientIdempotencyKey` (Unique filtered), `IX_WithdrawalRequests_Status_CreatedAt`.

#### 43. `Wallet.Infrastructure/Persistence/Migrations/20261117100000_AddWithdrawalWorkflowFields.cs` — `Added` (untracked)
- **Chi tiết:** EF Core Migration thêm 4 cột (`ClientIdempotencyKey`, `CompletedAt`, `RequestFingerprint`, `StatusReason`) vào `WithdrawalRequests` và tạo 3 index mới (kèm `Down` hoàn tác).

---

### E. Tests (`Wallet.Tests`)

#### File test được chỉnh sửa (`Modified`)
1. `Wallet.Tests/StripeCheckoutClientTests.cs`: Cập nhật `StripeCheckoutRequest` dùng `topupId.ToString("D")`, bỏ assertion `kind=topup`/`kind=old`, bỏ 2 test kiểm tra local HTTP theo environment (chuyển sang `StripeSettingsTests`).
2. `Wallet.Tests/StripeDirectPreparationTests.cs` *(unstaged)*: Bổ sung test `DirectCheckout_MissingWallet_AutoCreatesWalletAndPreparesSession`.
3. `Wallet.Tests/StripeTopupPreparationTests.cs` *(unstaged)*: Bổ sung test `Topup_MissingWallet_AutoCreatesWalletAndPreparesSession`.
4. `Wallet.Tests/StripeTopupRecoveryTests.cs`: Cập nhật `ResumeTopupCommandHandler(context)` bỏ tham số `provider`, sửa test case hết hạn/không thể resume sang gán `attempt.Status = StripePaymentAttemptStatus.Expired`.
5. `Wallet.Tests/WalletContractRegressionTests.cs`: Bỏ `ReferenceCode` khỏi `new ProcessWithdrawalCommand("approve")`.
6. `Wallet.Tests/WalletOperationsEdgeCaseTests.cs`: Cập nhật helper `Process(...)` dùng `TransferConfirmed`/`TransferReference`, bỏ kỳ vọng che số tài khoản trong `WalletTransaction.Note` và cập nhật mã lỗi `WITHDRAWAL_INVALID_TRANSITION` (409).
7. `Wallet.Tests/WalletRequestValidatorTests.cs`: Cập nhật test validation cho `RequestWithdrawalCommand` (thêm `IdempotencyKey`) và `ProcessWithdrawalCommand` (`TransferReference`, `AdminNote`).
8. `Wallet.Tests/WalletUseCaseBehaviorTests.cs`: Cập nhật `ProcessWithdrawalCommand` cho `APPROVE` và `Reject`.

#### File test được thêm mới (`Added`)
9. `Wallet.Tests/StripeSettingsTests.cs`: Kiểm thử `StripeSettings` (HTTPS/HTTP localhost theo environment, nối `transactionId`, `Validate`/`EnsureValid`).
10. `Wallet.Tests/StripeTopupHistoryTests.cs`: Kiểm thử `StripeTopupHistory` (`Evaluate`, `ListAsync`, `ResumeAsync`).
11. `Wallet.Tests/WalletFreezeTests.cs`: Kiểm thử `FreezeWalletCommandHandler`, `UnfreezeWalletCommandHandler` và `RequireInternalServiceKeyAttribute`.
12. `Wallet.Tests/WithdrawalWorkflowTests.cs`: Kiểm thử toàn diện `WithdrawalWorkflow`, `Idempotency-Key` khi tạo yêu cầu rút tiền và luồng tiền qua 6 hành động.

---

## 3. Các thay đổi bổ sung trong `nexus-dev-be\WalletService` (`feature/auction-wallet-holds`)

- **Đường dẫn:** `C:\Users\ACER\Desktop\nexus-dev-be\nexus-dev-be\WalletService`
- **Nhánh hiện tại:** `feature/auction-wallet-holds` (`[origin/dev: ahead 4, behind 2]`)
- **4 commit ahead so với `origin/dev` (44 file thay đổi so với merge-base `origin/dev...HEAD`):**
  1. `96c941e` — *fix(wallet): Stripe return URLs per top-up, local HTTP only in Development, validate Stripe settings at start-up* (7 file: `.env.example`, `Program.cs`, `StripeTopupPreparation.cs`, `IStripeCheckoutClient.cs`, `StripeCheckoutClient.cs`, `StripeSettings.cs`, `StripeSettingsTests.cs`).
  2. `abe4d41` — *feat(wallet): list own Stripe top-ups and resume an open payment* (7 file: `WalletsController.cs`, `StripeTopupHistory.cs`, `ResumeTopupCommand.cs`/`Handler.cs`, `ListTopupsQuery.cs`/`Handler.cs`, `StripeTopupHistoryTests.cs`).
  3. `0a54845` — *feat(wallet): withdrawal workflow - idempotent requests, seller and staff lists, history, Processing/Complete/Fail/ResolveFailed* (17 file: `AdminWalletsController.cs`, `WalletsController.cs`, `ProcessWithdrawalCommand`/`Handler`, `RequestWithdrawalCommand`/`Handler`, `WithdrawalQueries.cs`, `WithdrawalReader.cs`, `WithdrawalWorkflow.cs`, `WithdrawalRequest.cs`, `AuditAction.cs`, `WithdrawalRequestConfiguration.cs`, migration `20261117100000_AddWithdrawalWorkflowFields.cs`, `WalletDbContextModelSnapshot.cs`, và các file test).
  4. `6708602` — *wip: local auction, deposit and inventory fixes (16/11/2026)* (17 file — **phần khác biệt chính so với `BE\WalletService`**):
     - Triển khai mô hình **Auction Wallet Holds** dựa trên `WalletHold` (thay vì mô hình `AuctionHold` / `AuctionHoldOperation` / `AuctionCaptureTransfer` trên `origin/dev` từ commit `246594a` mà `BE` đang kế thừa):
       - **API & Filter:** `Wallet.Api/Filters/RequireHoldServiceKeyAttribute.cs` (`Added`) và 7 endpoint holds mới trên `Wallet.Api/Controllers/Public/WalletsController.cs` (`POST holds`, `GET holds/by-reference/{referenceType}/{referenceId}`, `GET holds/{holdId}`, `POST holds/{holdId}/release`, `POST holds/{holdId}/capture`, `POST holds/{holdId}/refund-captured`, `GET me/holds`).
       - **Application:** `Wallet.Application/Features/Holds/HoldContracts.cs`, `Wallet.Application/Features/Holds/HoldHandlers.cs` (275 dòng), `PayOrderWithWalletCommandHandler.cs` (kiểm tra `AvailableBalance`), `SettleSellerOrderCommandHandler.cs` (xử lý release tiền cọc từ `EscrowUserId` và trích phần chênh lệch sang `PlatformUserId` khi `hold.Amount > request.Amount`).
       - **Domain & Persistence:** `WalletHold.cs`, `WalletHoldStatus.cs`, `AuditAction.cs` (`AuctionHoldCreated`..`AuctionHoldRefunded`), `IWalletDbContext.cs` & `WalletDbContext.cs` (`WalletHolds`, `LockHoldReferenceAsync`), `WalletAccountConfiguration.cs` (`CK_Wallets_BalanceLocks`), `WalletHoldConfiguration.cs`, migration `20261116100000_AddAuctionWalletHolds.cs`, `WalletDbContextModelSnapshot.cs`.
       - **Tests:** `Wallet.Tests/AuctionHoldTests.cs`, `Wallet.Tests/Support/TestWalletDbContext.cs`.

### Điểm khác biệt chính giữa `BE\WalletService` và `nexus-dev-be\WalletService`
- **`BE\WalletService` có nhưng `nexus-dev-be` chưa có:**
  - Bộ tính năng **Freeze/Unfreeze ví** (`AdminWalletsController` `{userId}/freeze`, `{userId}/unfreeze`, `GET {userId}`; `InternalWalletsController` `freeze-purchase`, `unfreeze-purchase`; `RequireInternalServiceKeyAttribute`; `FreezeWalletCommand`/`Handler`; `UnfreezeWalletCommand`/`Handler`; `WalletAccount.Freeze()`/`Unfreeze()`; `WalletFreezeTests`).
  - `WalletAccountExtensions.LockOrEnsureWalletByUserIdAsync` / `StageDefaultWallet` (auto-create ví cho `SettleSellerOrder`, `EnsureWallet`, `StripeDirectPreparation`, `StripeTopupPreparation`).
  - Các cập nhật trên mô hình `AuctionHold` của `origin/dev`: `RequireAuctionServiceKeyAttribute` fallback `InternalSecurity:ServiceKey`, `CreateHoldCommandHandler` cập nhật `Status` khi replay, `ReleaseHoldCommand` thêm `"BUYER_WITHDRAWN"`.
  - Gỡ bỏ `StripeTopupReconciliationWorker.cs` và cập nhật `GetTopupStatusQuery`/`Handler`.
- **`nexus-dev-be\WalletService` (`feature/auction-wallet-holds`) có nhưng `BE` không dùng:**
  - Toàn bộ nhánh `WalletHold` / `HoldContracts` / `HoldHandlers` / `RequireHoldServiceKeyAttribute` / `20261116100000_AddAuctionWalletHolds` trong commit `6708602` (vốn là snapshot local ngày 16/11 trước khi `origin/dev` merge `246594a` với thiết kế `AuctionHold`).


---

# PHẦN II: ĐỐI SOÁT TOÀN BỘ API HIỆN CÓ & API CÒN THIẾU

# Báo Cáo Kiểm Toán & Đối Soát Toàn Diện API — `WalletService`

> **Phạm vi kiểm toán:** `C:\Users\ACER\Desktop\BE\WalletService` đối chiếu với `C:\Users\ACER\Desktop\nexus-dev-be\nexus-dev-be\WalletService`, cùng các caller trực tiếp tại `CommerceService`, `UserService`, `AuctionService` và cấu hình định tuyến tại `Gateway\Gateway\appsettings.json`.

---

## Phần 1: Bảng Đối Soát Toàn Bộ API Hiện Có

### 1.1. `WalletsController` (`[Route("api/v1/wallets")]` — `Wallet.Api/Controllers/Public/WalletsController.cs`)

> **Lưu ý Gateway:** Tất cả route dưới `/api/v1/wallets/...` đều đi qua `wallets-route` (`/api/v1/wallets/{**catch-all}` -> `wallet-cluster`) trong `Gateway\Gateway\appsettings.json`.

| # | Method & Route | Action / Handler | Auth Filter | Gateway | Nghiệp vụ / Logic | Chi tiết kiểm toán |
|---|---|---|---|---|---|---|
| 1 | `GET /api/v1/wallets/me` | `GetMyWallet` / `GetMyWalletQueryHandler` | `[RequireRole]` | Public (`wallets-route`) | `OK` | Đọc `x-user-id`, tự động `EnsureWalletCommand` nếu chưa có ví. |
| 2 | `GET /api/v1/wallets/transactions` | `ListTransactions` / `ListTransactionsQueryHandler` | `[RequireRole]` | Public (`wallets-route`) | `OK` | Phân trang, filter `type`, `fromDate`, `toDate` theo `UserId`. |
| 3 | `POST /api/v1/wallets/transfers` | `Transfer` / `TransferCommandHandler` | `[RequireRole]` | Public (`wallets-route`) | `LỖI / CẦN SỬA` | `TransferCommandHandler` **không kiểm tra `Status == "FROZEN"`** trên `senderWallet` (và `recipientWallet`), khiến ví bị đóng băng do tranh chấp vẫn chuyển tiền đi được. |
| 4 | `POST /api/v1/wallets/topups/stripe` | `TopupStripe` / `TopupStripeCommandHandler` | `[RequireRole]` | Public (`wallets-route`) | `OK` | Tạo phiên nạp Stripe kèm `Idempotency-Key`. |
| 5 | `GET /api/v1/wallets/topups` | `ListTopups` / `ListTopupsQueryHandler` | `[RequireRole]` | Public (`wallets-route`) | `OK` | Danh sách phiên nạp Stripe của user. |
| 6 | `GET /api/v1/wallets/topups/{id:guid}` | `GetTopupStatus` / `GetTopupStatusQueryHandler` | `[RequireRole]` | Public (`wallets-route`) | `OK` | Xem trạng thái 1 phiên nạp Stripe thuộc user. |
| 7 | `POST /api/v1/wallets/topups/{id:guid}/resume` | `ResumeTopup` / `ResumeTopupCommandHandler` | `[RequireRole]` | Public (`wallets-route`) | `OK` | Trả về `CheckoutUrl` còn hạn của phiên `Pending`. |
| 8 | `POST /api/v1/wallets/stripe/webhooks` | `StripeWebhook` / `ProcessStripeWebhookCommandHandler` | `[AllowAnonymous]` | Public (`wallets-route`) | `OK` | Xác thực chữ ký `Stripe-Signature` trong handler. |
| 9 | `POST /api/v1/wallets/checkout/direct` | `CheckoutDirect` / `CheckoutDirectCommandHandler` | `[RequireRole]` | Public (`wallets-route`) | `OK` | Tạo phiên Stripe Direct Checkout; thiếu endpoint `resume` và `cancel` tương ứng như `topups`. |
| 10 | `GET /api/v1/wallets/checkout/direct/{orderId}` | `GetDirectPaymentStatus` / `GetDirectPaymentStatusQueryHandler` | `[RequireRole]` | Public (`wallets-route`) | `OK` | Cho phép chủ đơn hoặc `admin`/`staff` (`AllowAnyUser`). |
| 11 | `POST /api/v1/wallets/withdrawals` | `Withdraw` / `RequestWithdrawalCommandHandler` | `[RequireRole("seller")]` + `[ReleaseFeature(Withdrawals)]` | Public (`wallets-route`) | `LỖI / CẦN SỬA` | (1) Handler **không kiểm tra `wallet.Status == "FROZEN"`**, ví bị đóng băng vẫn rút tiền được. (2) Entity `BankAccount` đã có trong `WalletDbContext.BankAccounts` nhưng không được dùng, ép nhập tay mỗi lần rút. (3) `WithdrawalStatus` thiếu `Cancelled`. |
| 12 | `GET /api/v1/wallets/withdrawals` | `ListWithdrawals` / `ListMyWithdrawalsQueryHandler` | `[RequireRole("seller")]` | Public (`wallets-route`) | `OK` | Danh sách lệnh rút của chính seller. |
| 13 | `GET /api/v1/wallets/withdrawals/{id:guid}` | `GetWithdrawal` / `GetMyWithdrawalQueryHandler` | `[RequireRole("seller")]` | Public (`wallets-route`) | `OK` | Chi tiết lệnh rút của chính seller. |
| 14 | `GET /api/v1/wallets/me/holds` | `ListMyHolds` / `ListMyHoldsQueryHandler` | `[RequireRole]` | Public (`wallets-route`) | `OK` | Danh sách tiền cọc đấu giá bị giữ của user. |
| 15 | `POST /api/v1/wallets/holds` | `CreateHold` / `CreateHoldCommandHandler` | `[RequireAuctionServiceKey]` | Exposed qua `wallets-route` | `OK` *(nên tách route)* | Có check `WALLET_FROZEN` (`Status != "ACTIVE"` -> `403`). Có `RequireAuctionServiceKey` nhưng đang đặt chung prefix public `/api/v1/wallets/...`. |
| 16 | `POST /api/v1/wallets/holds/{holdId:guid}/release` | `ReleaseHold` / `ReleaseHoldCommandHandler` | `[RequireAuctionServiceKey]` | Exposed qua `wallets-route` | `OK` *(nên tách route)* | Idempotent qua `Idempotency-Key`, mở khóa `LockedBalance`. |
| 17 | `POST /api/v1/wallets/holds/{holdId:guid}/capture` | `CaptureHold` / `CaptureHoldCommandHandler` | `[RequireAuctionServiceKey]` + `[ReleaseFeature(AuctionOrderCapture)]` | Exposed qua `wallets-route` | `OK` *(nên tách route)* | Chỉ bật `AuctionOrderCapture` khi `Purpose == "ORDER_PAYMENT"` và config cho phép (mặc định `false` ngoài `Development`). |
| 18 | `POST /api/v1/wallets/holds/{holdId:guid}/refund-captured` | `RefundCapturedHold` / `RefundCapturedHoldCommandHandler` | `[RequireAuctionServiceKey]` | Exposed qua `wallets-route` | `OK` *(nên tách route)* | Hoàn tiền cọc đã capture cho `ORDER_PAYMENT` từ `AuctionDepositEscrow`. |
| 19 | `GET /api/v1/wallets/holds/{holdId:guid}` | `GetHold` / `GetHoldQueryHandler` | `[RequireAuctionServiceKey]` | Exposed qua `wallets-route` | `OK` *(nên tách route)* | Tra cứu hold theo `holdId`. |
| 20 | `GET /api/v1/wallets/holds/by-reference/{referenceType}/{referenceId}` | `GetHoldByReference` / `GetHoldQueryHandler` | `[RequireAuctionServiceKey]` | Exposed qua `wallets-route` | `OK` *(nên tách route)* | Tra cứu hold theo `referenceType` + `referenceId`. |
| 21 | `POST /api/v1/wallets/orders/{orderId:guid}/payment` | `PayOrder` / `PayOrderWithWalletCommandHandler` | **Không có `[RequireRole]` / `[RequireInternalServiceKey]`** (chỉ `TryGetUserId`) | **Rò rỉ qua `wallets-route`** | `LỖI / CẦN SỬA` | (1) Route S2S đặt trên public controller và không có `[RequireInternalServiceKey]` hay `[RequireRole]`, chỉ đọc header `x-user-id` (dễ bị spoof nếu gọi nội bộ trực tiếp, và bị lộ ra ngoài qua Gateway). (2) `PayOrderWithWalletCommandHandler` **không kiểm tra `wallet.Status == "FROZEN"`**. |
| 22 | `POST /api/v1/wallets/sub-orders/{subOrderId:guid}/settle` | `SettleSellerOrder` / `SettleSellerOrderCommandHandler` | **Không có Auth Filter** (chỉ `[ReleaseFeature(LegacyCommerceCredits)]`) | **Rò rỉ qua `wallets-route`** | `LỖI / CẦN SỬA` | Lỗ hổng bảo mật P0: Không có bất kỳ xác thực nào (`[RequireInternalServiceKey]` / `[RequireRole]`), lại nằm dưới catch-all Gateway. Khi `LegacyCommerceCredits` bật (hoặc môi trường `Development`), bất kỳ ai cũng có thể gọi để cộng tiền vào ví seller bất kỳ. |
| 23 | `POST /api/v1/wallets/orders/{orderId:guid}/refund` | `RefundOrder` / `RefundOrderPaymentCommandHandler` | **Không có Auth Filter** (chỉ `[ReleaseFeature(LegacyCommerceCredits)]`) | **Rò rỉ qua `wallets-route`** | `LỖI / CẦN SỬA` | Lỗ hổng bảo mật P0: Không có auth filter, lộ qua Gateway catch-all. Cần đưa sang `InternalWalletsController` với `[RequireInternalServiceKey]`. |
| 24 | `POST /api/v1/wallets/sub-orders/{subOrderId:guid}/refund` | `RefundSubOrder` / `RefundOrderPaymentCommandHandler` | **Không có Auth Filter** (chỉ `[ReleaseFeature(LegacyCommerceCredits)]`) | **Rò rỉ qua `wallets-route`** | `LỖI / CẦN SỬA` | Lỗ hổng bảo mật P0: Không có auth filter, lộ qua Gateway catch-all. Ngoài ra với `IsSubOrder = true`, handler bỏ qua kiểm tra `PurchaseDebit` gốc (`if (!request.IsSubOrder)`), làm tăng rủi ro nếu bị gọi trái phép. |

### 1.2. `AdminWalletsController` (`[Route("api/v1/admin/wallets")]`, `[RequireRole("admin", "staff")]` — `Wallet.Api/Controllers/Public/AdminWalletsController.cs`)

> **Lưu ý Gateway:** Tất cả route dưới `/api/v1/admin/wallets/...` đi qua `admin-wallets-route` (`/api/v1/admin/wallets/{**catch-all}` -> `wallet-cluster`).

| # | Method & Route | Action / Handler | Auth Filter | Gateway | Nghiệp vụ / Logic | Chi tiết kiểm toán |
|---|---|---|---|---|---|---|
| 1 | `GET /api/v1/admin/wallets/withdrawals` | `ListWithdrawals` / `ListAdminWithdrawalsQueryHandler` | `[RequireRole("admin", "staff")]` | Public (`admin-wallets-route`) | `OK` | Danh sách yêu cầu rút tiền cho Admin/Staff. |
| 2 | `GET /api/v1/admin/wallets/withdrawals/{id:guid}/history` | `GetWithdrawalHistory` / `GetWithdrawalHistoryQueryHandler` | `[RequireRole("admin", "staff")]` | Public (`admin-wallets-route`) | `OK` | Lịch sử xử lý lệnh rút (không lộ thông tin ngân hàng). |
| 3 | `PATCH /api/v1/admin/wallets/withdrawals/{id:guid}` | `ProcessWithdrawal` / `ProcessWithdrawalCommandHandler` | `[RequireRole("admin", "staff")]` + check `ActorIsAdmin` cho `ResolveFailed` | Public (`admin-wallets-route`) | `OK` | Xử lý duyệt/từ chối/chuyển khoản/hoàn thất bại; `ResolveFailed` chặn `staff` (`403`). |
| 4 | `GET /api/v1/admin/wallets/ledger` | `ListLedger` / `ListWalletLedgerQueryHandler` | `[RequireRole("admin", "staff")]` + `this.EnsureAdmin(...)` | Public (`admin-wallets-route`) | `OK` | Admin-only (Staff bị trả `403`). |
| 5 | `GET /api/v1/admin/wallets/reconciliation` | `ReconcileWallets` / `GetWalletReconciliationQueryHandler` | `[RequireRole("admin", "staff")]` + `this.EnsureAdmin(...)` | Public (`admin-wallets-route`) | `OK` | Admin-only (Staff bị trả `403`). |
| 6 | `POST /api/v1/admin/wallets/{userId:guid}/adjustments` | `AdjustBalance` / `AdjustWalletBalanceCommandHandler` | `[RequireRole("admin", "staff")]` *(thiếu `EnsureAdmin`)* | Public (`admin-wallets-route`) | `LỖI / CẦN SỬA` | **Thiếu `this.EnsureAdmin(...)`**: Hiện cho phép cả `staff` tự ý cộng/trừ số dư (`Credit`/`Debit`) ví user. Cần bổ sung `this.EnsureAdmin(out var authError)` (trừ khi nghiệp vụ xác nhận cho phép staff điều chỉnh tiền). |
| 7 | `GET /api/v1/admin/wallets/{userId:guid}` | `GetUserWallet` / `GetMyWalletQueryHandler` | `[RequireRole("admin", "staff")]` | Public (`admin-wallets-route`) | `OK` | Xem chi tiết ví theo `userId` (lưu ý: sẽ tự tạo ví nếu user chưa có). |
| 8 | `POST /api/v1/admin/wallets/{userId:guid}/freeze` | `FreezeWallet` / `FreezeWalletCommandHandler` | `[RequireRole("admin", "staff")]` | Public (`admin-wallets-route`) | `OK` | Đóng băng ví user (ghi `AuditLog`). |
| 9 | `POST /api/v1/admin/wallets/{userId:guid}/unfreeze` | `UnfreezeWallet` / `UnfreezeWalletCommandHandler` | `[RequireRole("admin", "staff")]` | Public (`admin-wallets-route`) | `OK` | Mở băng ví user (ghi `AuditLog`). |

### 1.3. `InternalWalletsController` (`[Route("api/v1/internal/wallets")]`, `[RequireInternalServiceKey]` — `Wallet.Api/Controllers/Internal/InternalWalletsController.cs`)

> **Lưu ý Gateway & Middleware:** Gateway **không** expose `/api/v1/internal/{**catch-all}`. Mọi request đến `/api/v1/internal/...` còn đi qua `InternalServiceAuthMiddleware` và `[RequireInternalServiceKey]` (kiểm tra header `X-Internal-Service-Key`).

| # | Method & Route | Action / Handler | Auth Filter | Gateway | Nghiệp vụ / Logic | Chi tiết kiểm toán |
|---|---|---|---|---|---|---|
| 1 | `POST /api/v1/internal/wallets/freeze-purchase` | `FreezePurchase` / `FreezeWalletCommandHandler` | `[RequireInternalServiceKey]` + `InternalServiceAuthMiddleware` | Internal-only | `OK` | Đóng băng ví khi có tranh chấp. |
| 2 | `POST /api/v1/internal/wallets/unfreeze-purchase` | `UnfreezePurchase` / `UnfreezeWalletCommandHandler` | `[RequireInternalServiceKey]` + `InternalServiceAuthMiddleware` | Internal-only | `OK` | Mở băng ví sau khi xử lý tranh chấp. |

> **Lưu ý nhánh `nexus-dev-be/WalletService` (`feature/auction-wallet-holds`):** Trên nhánh đó chưa có `InternalWalletsController`, chưa có `AdminWalletsController` (`ledger`, `reconciliation`, `freeze`, `unfreeze`, `GetUserWallet`), và `holds` dùng `[RequireHoldServiceKey]`. Bản trên `BE/WalletService` đã hợp nhất `holds` (`[RequireAuctionServiceKey]`) và bổ sung `InternalWalletsController` + `AdminWalletsController` mở rộng.

---

## Phần 2: Đối Soát S2S Contract

| Caller Service & File | Endpoint đang gọi ở `WalletService` | Header / Auth hiện tại | Trạng thái đối soát | Vấn đề & Khuyến nghị |
|---|---|---|---|---|
| **`CommerceService`** (`Commerce.Infrastructure/Services/Wallet/WalletClient.cs`) | `GET api/v1/wallets/me` (`GetWalletBalanceAsync`) | `x-user-id: <userId>` + `X-Internal-Service-Key` (mặc định trên `HttpClient`) | `LỆCH KIẾN TRÚC` | Phải giả lập `x-user-id` gọi vào route public `/me` (và chỉ lấy `Balance`, chưa đọc `AvailableBalance` hay `Status == FROZEN`). Cần chuyển sang `GET /api/v1/internal/wallets/{userId:guid}`. |
| **`CommerceService`** (`WalletClient.cs`) | `POST api/v1/wallets/orders/{orderId}/payment` (`PayOrderAsync`) | `x-user-id: <buyerId>` + `X-Internal-Service-Key` | `NGUY HIỂM (P0)` | Endpoint đang nằm trên `WalletsController` (public route) và không check `X-Internal-Service-Key`. Cần chuyển sang `POST /api/v1/internal/wallets/orders/{orderId:guid}/payment` (`[RequireInternalServiceKey]`). |
| **`CommerceService`** (`WalletClient.cs`) | `POST api/v1/wallets/sub-orders/{subOrderId}/settle` (`SettleSellerSubOrderAsync`) | `X-Internal-Service-Key` (nhưng server không check!) | `NGUY HIỂM (P0)` | Endpoint không có auth filter trên `WalletsController`, lộ qua Gateway. Cần chuyển sang `POST /api/v1/internal/wallets/sub-orders/{subOrderId:guid}/settle` (`[RequireInternalServiceKey]`). |
| **`CommerceService`** (`WalletClient.cs`) | `POST api/v1/wallets/orders/{orderId}/refund` (`RefundOrderPaymentAsync`) | `X-Internal-Service-Key` (server không check!) | `NGUY HIỂM (P0)` | Tương tự `settle`. Cần chuyển sang `POST /api/v1/internal/wallets/orders/{orderId:guid}/refund`. |
| **`CommerceService`** (`WalletClient.cs`) | `POST api/v1/wallets/sub-orders/{subOrderId}/refund` (`RefundSubOrderPaymentAsync`) | `X-Internal-Service-Key` (server không check!) | `NGUY HIỂM (P0)` | Tương tự `settle`. Cần chuyển sang `POST /api/v1/internal/wallets/sub-orders/{subOrderId:guid}/refund`. |
| **`CommerceService`** (`WalletClient.cs`) | `GET api/v1/wallets/holds/by-reference/AUCTION_DEPOSIT/{depositId}` (`GetAuctionDepositHoldAsync`) | `X-Internal-Service-Key` | `OK` *(nên tách route)* | Server kiểm tra `[RequireAuctionServiceKey]`. Nên có alias `/api/v1/internal/wallets/holds/...` để không phụ thuộc route public. |
| **`UserService`** (`User.Infrastructure/Services/External/WalletServiceClient.cs`) | `POST api/v1/internal/wallets/freeze-purchase` & `unfreeze-purchase` | `X-Internal-Service-Key` | `OK` | Khớp hoàn toàn với `InternalWalletsController`. |
| **`AuctionService`** (`Auction.Infrastructure/Clients/WalletClient.cs`) | `POST/GET /api/v1/wallets/holds/...` (`Create`, `Get`, `GetByReference`, `Release`, `Capture`, `RefundCaptured`) | `X-Internal-Service-Key` + `Idempotency-Key` | `OK` *(nên tách route)* | Contract request/response & error codes (`HOLD_ALREADY_CAPTURED`, `HOLD_EXPIRED`, `WALLET_FROZEN`) khớp với `WalletsController`. Nên mirror sang `/api/v1/internal/wallets/holds/...` và chặn ở Gateway. |

---

## Phần 3: Danh Sách API Còn Thiếu

### A. Public API (Frontend / User / Seller / Admin / Staff)

#### 1. Quản lý tài khoản ngân hàng đã lưu của Seller (Saved Bank Accounts)
- **Proposed Endpoints:**
  - `GET /api/v1/wallets/bank-accounts`
  - `POST /api/v1/wallets/bank-accounts`
  - `PATCH /api/v1/wallets/bank-accounts/{id:guid}/default`
  - `DELETE /api/v1/wallets/bank-accounts/{id:guid}`
- **Caller / Auth:** `seller` (`[RequireRole("seller")]`)
- **Priority:** `P1 - Quan trọng / UX như Shopee-TikTok`
- **Purpose & End-to-End Flow:**
  - Hiện tại trong `Wallet.Domain/Entities/BankAccount.cs` và `WalletDbContext.BankAccounts` đã có sẵn entity & bảng `BankAccounts` (`UserId`, `BankName`, `AccountNumber`, `AccountHolderName`, `IsDefault`), nhưng hoàn toàn **không có controller endpoint** nào đọc/ghi bảng này.
  - `RequestWithdrawalCommand` hiện bắt buộc seller truyền đủ `BankCode`, `AccountNumber`, `AccountHolderName` mỗi lần rút tiền.
  - Khi bổ sung CRUD BankAccount và cho phép `RequestWithdrawalCommand` nhận thêm `BankAccountId?` (fallback lấy tài khoản `IsDefault = true` nếu không nhập tay), seller chỉ cần liên kết ngân hàng một lần và chọn nhanh khi rút tiền.
  - **Validation:** Đảm bảo mỗi user chỉ có tối đa 1 `IsDefault = true`; không cho xóa tài khoản đang được tham chiếu bởi `WithdrawalRequest` ở trạng thái `Pending`/`Approved`/`Processing` (hoặc snapshot thông tin ngân hàng lên `WithdrawalRequest` như hiện tại đã làm).

#### 2. Seller tự hủy yêu cầu rút tiền đang chờ duyệt
- **Proposed Endpoint:** `POST /api/v1/wallets/withdrawals/{id:guid}/cancel`
- **Caller / Auth:** `seller` (`[RequireRole("seller")]`)
- **Priority:** `P1 - Quan trọng / UX như Shopee-TikTok`
- **Purpose & End-to-End Flow:**
  - Hiện tại `WithdrawalStatus` (`Wallet.Domain/Enums/WithdrawalStatus.cs`) chỉ có `Pending, Approved, Processing, Completed, Rejected, Failed`. Khi seller tạo nhầm số tiền hoặc sai số tài khoản ở trạng thái `Pending`, số tiền đã bị khóa vào `WalletAccount.LockedBalance`, nhưng seller không có cách nào tự hủy mà phải chờ Admin/Staff `Reject`.
  - Endpoint này chỉ cho phép hủy khi `WithdrawalRequest.WalletId` thuộc `currentUserId` và `Status == WithdrawalStatus.Pending`; thực hiện chuyển sang `WithdrawalStatus.Cancelled` (bổ sung enum), gọi `wallet.ReleaseLockedFunds(amount + fee)`, và ghi `AuditLog`. Trả về `409 Conflict` nếu admin đã `Approve` hoặc đang `Processing`.

#### 3. Xem chi tiết một giao dịch ví theo ID
- **Proposed Endpoint:** `GET /api/v1/wallets/transactions/{id:guid}`
- **Caller / Auth:** `buyer`, `seller` (`[RequireRole]`)
- **Priority:** `P1 - Quan trọng / UX như Shopee-TikTok`
- **Purpose & End-to-End Flow:**
  - `WalletsController` hiện mới có `GET /api/v1/wallets/transactions` (danh sách phân trang). Khi người dùng bấm vào một dòng biến động số dư hoặc deep-link từ Notification / Order detail để xem biên lai chi tiết (`BalanceBefore`, `BalanceAfter`, `ReferenceId`, `Note`, trạng thái gateway), FE không có endpoint lấy 1 record theo `id`.
  - **Validation:** Chỉ trả về giao dịch thuộc `WalletId` / `UserId` của caller; trả `404` nếu không tồn tại hoặc không thuộc sở hữu.

#### 4. Stripe Direct Checkout: Resume & Cancel phiên thanh toán trực tiếp
- **Proposed Endpoints:**
  - `POST /api/v1/wallets/checkout/direct/{orderId:guid}/resume`
  - `POST /api/v1/wallets/checkout/direct/{orderId:guid}/cancel`
- **Caller / Auth:** `buyer` (`[RequireRole]`)
- **Priority:** `P1 - Quan trọng / UX như Shopee-TikTok`
- **Purpose & End-to-End Flow:**
  - Đối với nạp tiền (`Topup`), `WalletsController` đã có đủ `GET /topups`, `POST /topups/{id:guid}/resume`, `GET /topups/{id:guid}`. Nhưng với thanh toán đơn hàng trực tiếp (`POST /checkout/direct`), hiện mới chỉ có `GET /checkout/direct/{orderId}`.
  - Nếu buyer lỡ đóng tab Stripe Checkout trước khi thanh toán, FE cần `POST /checkout/direct/{orderId:guid}/resume` để lấy lại `CheckoutUrl` còn hạn từ `StripePaymentAttempt` (`AwaitingPayment`) mà không tạo trùng session.
  - Nếu buyer đổi ý muốn chuyển sang thanh toán bằng số dư ví (`PayOrder`) hoặc hủy đơn khi chưa trả, hiện `PayOrderWithWalletCommandHandler` sẽ ném `409 PAYMENT_ORDER_CONFLICT` nếu còn `StripePaymentAttempt` có `BlocksReplacement == true`. Cần endpoint `cancel` (kiểm tra với Stripe rằng session chưa `paid`, đóng session/attempt về `Cancelled`/`Failed`) để giải phóng đơn hàng.

#### 5. Admin/Staff: Danh sách ví, quản lý Auction Holds & xử lý Stripe `ReconciliationRequired`
- **Proposed Endpoints:**
  - `GET /api/v1/admin/wallets` *(Caller: `admin`, `staff` — `[RequireRole("admin", "staff")]`)*
    - Danh sách phân trang tất cả ví, hỗ trợ filter `status` (`ACTIVE`/`FROZEN`), `userId`, `search`. Hiện `AdminWalletsController` chỉ có `GET /{userId:guid}`, không có màn hình list ví cho CSKH/Admin kiểm tra các ví đang bị `FROZEN`. (`P2 - Vận hành / Mở rộng`)
  - `GET /api/v1/admin/wallets/holds` & `GET /api/v1/admin/wallets/{userId:guid}/holds` *(Caller: `admin`, `staff`)*
    - Tra cứu các khoản tiền cọc đấu giá đang bị giữ (`AuctionHold`) của user để xử lý khiếu nại hoặc kiểm tra lý do `AvailableBalance < Balance`. (`P1 - Quan trọng / UX như Shopee-TikTok`)
  - `GET /api/v1/admin/wallets/stripe/reconciliation-required` & `POST /api/v1/admin/wallets/stripe/attempts/{attemptId:guid}/resolve` *(Caller: `admin` — `EnsureAdmin`)*
    - Trong `ProcessStripeWebhookCommandHandler` và `ReconcilePendingTopupsCommandHandler`, khi phát hiện sai lệch giữa Stripe và DB (`StripeSettlementOutcome.ReconciliationRequired`), hệ thống chỉ `LogCritical` mà chưa có API cho Admin xem danh sách các `StripePaymentAttempt` bị treo để đối soát/đóng thủ công. (`P1 - Quan trọng / UX như Shopee-TikTok`)
  - `POST /api/v1/admin/wallets/stripe/reconcile-pending` *(Caller: `admin` — `EnsureAdmin`)*
    - Cho phép Admin chủ động kích hoạt đối soát các phiên `Topup` và `DirectOrder` đang `Pending`. (`P2 - Vận hành / Mở rộng`)

---

### B. Internal API (Service-to-Service)

#### 1. Internal tra cứu số dư & trạng thái ví theo `userId`
- **Proposed Endpoint:** `GET /api/v1/internal/wallets/{userId:guid}`
- **Caller / Auth:** `CommerceService`, `UserService`, `AuctionService` (`[RequireInternalServiceKey]`)
- **Priority:** `P0 - Bắt buộc cho flow chính`
- **Purpose & End-to-End Flow:**
  - Thay thế việc `CommerceService.WalletClient.GetWalletBalanceAsync` phải gọi `GET /api/v1/wallets/me` kèm header giả lập `x-user-id: <userId>`.
  - Trả về đầy đủ `Balance`, `AvailableBalance`, `LockedBalance`, `Status` (`ACTIVE`/`FROZEN`) để caller kiểm tra trước khi đặt hàng/rút tiền/tham gia đấu giá.

#### 2. Tách các endpoint Payment / Settle / Refund của `CommerceService` sang Internal Controller
- **Proposed Endpoints:**
  - `POST /api/v1/internal/wallets/orders/{orderId:guid}/payment` (Body: `{ buyerId, amount, idempotencyKey? }`)
  - `POST /api/v1/internal/wallets/sub-orders/{subOrderId:guid}/settle` (Body: `{ sellerId, amount, idempotencyKey? }`)
  - `POST /api/v1/internal/wallets/orders/{orderId:guid}/refund` (Body: `{ userId, amount, reason, idempotencyKey? }`)
  - `POST /api/v1/internal/wallets/sub-orders/{subOrderId:guid}/refund` (Body: `{ userId, amount, reason, idempotencyKey? }`)
- **Caller / Auth:** `CommerceService` (`[RequireInternalServiceKey]`)
- **Priority:** `P0 - Bắt buộc cho flow chính`
- **Purpose & End-to-End Flow:**
  - Khắc phục lỗ hổng bảo mật P0 khi 4 endpoint này đang đặt trên `WalletsController` (không có `[RequireInternalServiceKey]`, riêng `settle` và `refund` không có bất kỳ auth filter nào).
  - Bổ sung kiểm tra `wallet.Status == "FROZEN"` trong `PayOrderWithWalletCommandHandler` (`403 WALLET_FROZEN`).

#### 3. Internal hủy/hết hạn Direct Payment Order khi đơn hàng quá hạn
- **Proposed Endpoint:** `POST /api/v1/internal/wallets/checkout/direct/{orderId:guid}/expire`
- **Caller / Auth:** `CommerceService` (`[RequireInternalServiceKey]`)
- **Priority:** `P1 - Quan trọng / UX như Shopee-TikTok`
- **Purpose & End-to-End Flow:**
  - `CommerceService` là service sở hữu vòng đời hết hạn đơn hàng (`Order expiry`), còn `WalletService` không tự expire `DirectPaymentOrder`.
  - Endpoint này cho phép `CommerceService` báo `orderId` đã quá hạn thanh toán để đánh dấu `DirectPaymentOrder` / `StripePaymentAttempt` hết hạn, tránh trường hợp user thanh toán muộn vào link Stripe cũ sau khi đơn hàng ở Commerce đã bị hủy.

#### 4. Chuẩn hóa route `holds` về namespace Internal (hoặc giữ alias tương thích)
- **Proposed Endpoints:** `/api/v1/internal/wallets/holds/...` (mirror cho `/api/v1/wallets/holds/...` hiện dùng `[RequireAuctionServiceKey]`)
- **Caller / Auth:** `AuctionService`, `CommerceService` (`[RequireAuctionServiceKey]` / `[RequireInternalServiceKey]`)
- **Priority:** `P2 - Vận hành / Mở rộng`
- **Purpose & End-to-End Flow:**
  - Tách hoàn toàn khỏi prefix public `/api/v1/wallets/{**catch-all}` của Gateway để giảm bề mặt tấn công.

---

### C. GHN Master Data & Buyer Address / Default Address / Change-Address Flow

1. **GHN Master Data (Province / District / Ward) & Buyer Address Book (`isDefault`):**
   - `WalletService` **không** sở hữu dữ liệu địa chỉ giao hàng hay GHN Master Data (thuộc `UserService` và `FulfillmentService`).
   - Không cần thêm API GHN Master Data hay Address Book tại `WalletService`.
2. **Checkout quote recalculation & Post-order Change-Address (trước khi bàn giao ĐVVC):**
   - Khi Buyer đổi địa chỉ ở màn hình Checkout (trước khi tạo thanh toán), `CommerceService` tính lại tổng tiền và chỉ gửi `Amount` cuối cùng sang `WalletService` (`PayOrder` hoặc `CheckoutDirect`) — flow hiện tại đã đáp ứng.
   - **Khoảng trống hiện tại (Post-order change-address sau khi đã thanh toán):** Nếu hệ thống cho phép Buyer đổi địa chỉ giao hàng *sau khi đã thanh toán thành công* (trước lúc bàn giao ĐVVC) và địa chỉ mới làm thay đổi phí vận chuyển (tăng thêm hoặc giảm đi):
     - `PayOrderWithWalletCommandHandler` và `RefundOrderPaymentCommandHandler` hiện ràng buộc mỗi `OrderId` chỉ có duy nhất 1 `PurchaseDebit` và 1 `RefundCredit` khớp `Amount` (`409 PAYMENT_ORDER_CONFLICT` / `REFUND_CONFLICT`).
     - Nếu hỗ trợ thu thêm / hoàn chênh lệch phí ship sau thanh toán, cần bổ sung:
       - `POST /api/v1/internal/wallets/orders/{orderId:guid}/adjust-payment` (`[RequireInternalServiceKey]`, `Idempotency-Key` bắt buộc, hỗ trợ `DeltaAmount > 0` thu thêm hoặc `DeltaAmount < 0` hoàn phần chênh lệch vào ví buyer). *(Priority: `P2 - Vận hành / Mở rộng`)*

---

### D. Gateway Routing & Security

Kiểm tra `C:\Users\ACER\Desktop\BE\Gateway\Gateway\appsettings.json`:
- Gateway đã cấu hình:
  - `wallets-route`: `/api/v1/wallets/{**catch-all}` -> `wallet-cluster`
  - `admin-wallets-route`: `/api/v1/admin/wallets/{**catch-all}` -> `wallet-cluster`
  - Không expose `/api/v1/internal/{**catch-all}`.

**Các điểm cần xử lý ngay tại Gateway & `WalletService`:**
1. **Route Public mới:** Các endpoint mới đề xuất dưới `/api/v1/wallets/...` (`bank-accounts`, `withdrawals/{id}/cancel`, `transactions/{id}`, `checkout/direct/{orderId}/resume|cancel`) và `/api/v1/admin/wallets/...` sẽ **tự động đi qua** `wallets-route` và `admin-wallets-route` hiện tại mà không cần thêm route mới ở Gateway.
2. **Chặn các route S2S bị rò rỉ qua `wallets-route` (Khẩn cấp - P0):**
   - Vì `wallets-route` dùng `{**catch-all}`, các route sau trên `WalletsController` hiện đang mở ra ngoài qua Gateway:
     - `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` (không có auth filter!)
     - `POST /api/v1/wallets/orders/{orderId}/refund` (không có auth filter!)
     - `POST /api/v1/wallets/sub-orders/{subOrderId}/refund` (không có auth filter!)
     - `POST /api/v1/wallets/orders/{orderId}/payment` (chỉ check `x-user-id`, không check `X-Internal-Service-Key`)
   - **Khuyến nghị:** Chuyển 4 endpoint này sang `InternalWalletsController` (`/api/v1/internal/wallets/...`), cập nhật `CommerceService/Commerce.Infrastructure/Services/Wallet/WalletClient.cs` gọi sang `/api/v1/internal/wallets/...` kèm `X-Internal-Service-Key`, và xóa (hoặc chặn 403/404 ở Gateway) các route S2S cũ trên `WalletsController`.
3. **Phân quyền `AdminWalletsController.AdjustBalance` (P0):**
   - `POST /api/v1/admin/wallets/{userId:guid}/adjustments` hiện chỉ có `[RequireRole("admin", "staff")]` ở cấp controller và **không gọi `this.EnsureAdmin(...)`** (khác với `ListLedger` và `ReconcileWallets`). Cần bổ sung `this.EnsureAdmin(out var authError)` để chặn `staff` tự ý cộng/trừ tiền.
4. **Thực thi trạng thái `FROZEN` đồng bộ (P0):**
   - Bổ sung kiểm tra `wallet.Status == "FROZEN"` (ném `403 WALLET_FROZEN`) vào `PayOrderWithWalletCommandHandler`, `TransferCommandHandler`, `RequestWithdrawalCommandHandler` (hiện mới chỉ `CreateHoldCommandHandler` kiểm tra).


---

# PHẦN III: KẾ HOẠCH TRIỂN KHAI & FIX CHI TIẾT

# Kế hoạch Sửa lỗi & Triển khai Chi tiết — `WalletService`

> **Tài liệu nguồn:** Đối chiếu từ `_ModifiedFiles/WalletService.md` và `_MissingAPIs/WalletService.md`.
> **Workspace chính:** `C:\Users\ACER\Desktop\BE\WalletService`
> **Mục tiêu:** Đồng bộ Git an toàn, vá ngay các lỗ hổng bảo mật S2S P0 trên `WalletsController` & `AdminWalletsController`, thực thi đồng bộ trạng thái `FROZEN`, hoàn thiện các API P0/P1 còn thiếu và phối hợp chặt chẽ với `CommerceService`, `AuctionService`, `UserService` và `Gateway`.

---

## Bước 0: Đồng bộ Git & Nhánh làm việc

### Trạng thái Git hiện tại
- **Repo chính (`BE\WalletService`):**
  - Nhánh hiện tại: `feature/stripe-topup-resume-and-reconciliation` (**KHÔNG** ở `dev`).
  - So với `origin/dev`: **Behind 2 | Ahead 2**.
  - 2 commit mới trên `origin/dev` **chưa** pull/merge:
    - `f55185e` — `fix(docker): correct environment variable names for Stripe integration`
    - `246594a` — `feat(wallet): Enhance wallet service with new financial operations and reporting features`
  - Working tree hiện có **55 file thay đổi** (37 `Modified`, 1 `Deleted`, 17 `Added`/untracked), bao gồm cả staged và unstaged (ví dụ `AdminWalletsController.cs`, `AuditAction.cs`, `StripeTopupPreparation.cs` có cả phần staged và unstaged).
  - *Lưu ý:* `IWalletDbContext.cs`, `WalletDbContext.cs`, `TestWalletDbContext.cs` có thay đổi tạm ở staged đã hoàn tác ở working tree (net diff = rỗng).
- **Repo tham chiếu (`nexus-dev-be\WalletService`):**
  - Nhánh `feature/auction-wallet-holds` (`ahead 4, behind 2` so với `origin/dev`), trong đó commit `6708602` dùng mô hình `WalletHold` cũ (snapshot ngày 16/11 trước khi `origin/dev` merge `246594a` với thiết kế `AuctionHold`). **Không merge** nhánh `6708602` vào `BE\WalletService` để tránh xung đột với `AuctionHold` / `AuctionHoldOperation` / `AuctionCaptureTransfer`.

### Lệnh đồng bộ an toàn (tránh mất thay đổi local)
Thực hiện tại `C:\Users\ACER\Desktop\BE`:

```powershell
# 1. Kiểm tra trạng thái hiện tại và dọn các file có net diff rỗng ở index
git status
git reset HEAD -- WalletService/Wallet.Application/Interfaces/IWalletDbContext.cs `
                 WalletService/Wallet.Infrastructure/Persistence/WalletDbContext.cs `
                 WalletService/Wallet.Tests/Support/TestWalletDbContext.cs

# 2. Stash toàn bộ thay đổi đang làm dở (bao gồm cả untracked như env, InternalWalletsController, migrations, tests...)
git stash push -u -m "wip-wallet-freeze-withdrawal-stripe"

# 3. Cập nhật origin/dev và rebase nhánh làm việc lên origin/dev
git fetch origin dev
git rebase origin/dev
# (Hoặc nếu đã có 2 commit local trùng với origin/dev, giải quyết conflict ưu tiên giữ thay đổi mới nhất rồi tiếp tục: git rebase --continue)

# 4. Khôi phục lại working tree từ stash
git stash pop

# 5. Kiểm tra lại build & test hiện có trước khi bắt đầu Phase 1
dotnet build WalletService/WalletService.sln
dotnet test WalletService/Wallet.Tests/Wallet.Tests.csproj
```

---

## Phase 1 (P0 - Khẩn cấp): Sửa lỗi nghiệp vụ, Bảo mật & Gãy S2S Contract

### 1.1. Vá lỗ hổng bảo mật P0: Tách 4 endpoint S2S ra khỏi `WalletsController` sang `InternalWalletsController`
- **Vấn đề:** `Gateway/Gateway/appsettings.json` expose `/api/v1/wallets/{**catch-all}` qua `wallets-route`. Hiện 4 endpoint S2S trên `Wallet.Api/Controllers/Public/WalletsController.cs` đang lộ ra ngoài:
  - `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` (`SettleSellerOrder`) — **không có Auth Filter**, chỉ có `[ReleaseFeature(LegacyCommerceCredits)]`.
  - `POST /api/v1/wallets/orders/{orderId}/refund` (`RefundOrder`) — **không có Auth Filter**.
  - `POST /api/v1/wallets/sub-orders/{subOrderId}/refund` (`RefundSubOrder`) — **không có Auth Filter** (và khi `IsSubOrder = true` bỏ qua check `PurchaseDebit` gốc).
  - `POST /api/v1/wallets/orders/{orderId}/payment` (`PayOrder`) — chỉ gọi `TryGetUserId` đọc header `x-user-id`, không có `[RequireInternalServiceKey]` hay `[RequireRole]`.
- **File cần sửa:**
  - `Wallet.Api/Controllers/Internal/InternalWalletsController.cs` (`[Route("api/v1/internal/wallets")]`, `[RequireInternalServiceKey]`)
  - `Wallet.Api/Controllers/Public/WalletsController.cs`
- **Thay đổi cụ thể:**
  1. Thêm 4 action mới vào `InternalWalletsController`:
     - `POST /api/v1/internal/wallets/orders/{orderId:guid}/payment` (nhận `buyerId`, `amount`, `idempotencyKey?` -> gửi `PayOrderWithWalletCommand`).
     - `POST /api/v1/internal/wallets/sub-orders/{subOrderId:guid}/settle` (nhận `sellerId`, `amount`, `idempotencyKey?` -> gửi `SettleSellerOrderCommand`).
     - `POST /api/v1/internal/wallets/orders/{orderId:guid}/refund` (nhận `userId`, `amount`, `reason`, `idempotencyKey?` -> gửi `RefundOrderPaymentCommand`).
     - `POST /api/v1/internal/wallets/sub-orders/{subOrderId:guid}/refund` (nhận `userId`, `amount`, `reason`, `idempotencyKey?` -> gửi `RefundOrderPaymentCommand` với `IsSubOrder = true`).
  2. Trên `WalletsController`:
     - **Xóa** `SettleSellerOrder`, `RefundOrder`, `RefundSubOrder`, `PayOrder` khỏi `WalletsController` (hoặc tạm thời gắn `[RequireInternalServiceKey]` nếu cần zero-downtime trong lúc chờ `CommerceService` deploy, nhưng **bắt buộc xóa hẳn** khỏi public controller ngay sau khi `CommerceService` chuyển URL để tránh rò rỉ qua `wallets-route`).

### 1.2. Bổ sung API Internal tra cứu số dư & trạng thái ví (`GET /api/v1/internal/wallets/{userId:guid}`)
- **Vấn đề:** `CommerceService` (`Commerce.Infrastructure/Services/Wallet/WalletClient.cs` — `GetWalletBalanceAsync`) hiện phải giả lập header `x-user-id: <userId>` gọi vào route public `GET /api/v1/wallets/me` và chưa đọc `AvailableBalance` / `Status == "FROZEN"`.
- **File cần sửa:** `Wallet.Api/Controllers/Internal/InternalWalletsController.cs`
- **Thay đổi cụ thể:**
  - Thêm endpoint `GET /api/v1/internal/wallets/{userId:guid}` (`GetUserWallet(Guid userId)`), gọi `GetMyWalletQuery(userId.ToString())` (hoặc query chuyên biệt trả về `WalletId`, `UserId`, `Balance`, `AvailableBalance`, `LockedBalance`, `Status`, `Currency`).
  - Bảo vệ bằng `[RequireInternalServiceKey]` ở cấp controller (cùng `InternalServiceAuthMiddleware`).

### 1.3. Thực thi đồng bộ trạng thái `wallet.Status == "FROZEN"` tại tất cả luồng chi tiêu/chuyển/rút tiền
- **Vấn đề:** Hiện mới chỉ `CreateHoldCommandHandler` kiểm tra `Status != "ACTIVE"` -> `403 WALLET_FROZEN`. Các handler khác vẫn cho phép ví bị đóng băng (`Status == "FROZEN"`) tiêu tiền, chuyển tiền và rút tiền.
- **File & Symbol cần sửa:**
  1. `Wallet.Application/Features/Checkout/Commands/PayOrderWithWallet/PayOrderWithWalletCommandHandler.cs`:
     - Sau khi lock ví (`wallet`), kiểm tra `if (wallet.Status == "FROZEN")` (hoặc `!= "ACTIVE"`), ném exception tương ứng trả về `403 WALLET_FROZEN`.
  2. `Wallet.Application/Features/Transfers/Commands/Transfer/TransferCommandHandler.cs` *(handler của `POST /api/v1/wallets/transfers`)*:
     - Kiểm tra `senderWallet.Status == "FROZEN"` (và cân nhắc chặn hoặc kiểm tra `recipientWallet.Status == "FROZEN"` theo nghiệp vụ tranh chấp) -> trả `403 WALLET_FROZEN`.
  3. `Wallet.Application/Features/Withdrawals/Commands/RequestWithdrawal/RequestWithdrawalCommandHandler.cs`:
     - Sau khi lock ví seller, kiểm tra `wallet.Status == "FROZEN"` -> trả `403 WALLET_FROZEN`.

### 1.4. Vá lỗ hổng phân quyền `AdminWalletsController.AdjustBalance`
- **Vấn đề:** `POST /api/v1/admin/wallets/{userId:guid}/adjustments` (`AdjustBalance`) trong `Wallet.Api/Controllers/Public/AdminWalletsController.cs` chỉ có `[RequireRole("admin", "staff")]` ở cấp controller, **thiếu** `this.EnsureAdmin(out var authError)` như `ListLedger` và `ReconcileWallets`, khiến `staff` có thể tự ý `Credit`/`Debit` số dư user.
- **File cần sửa:** `Wallet.Api/Controllers/Public/AdminWalletsController.cs`
- **Thay đổi cụ thể:**
  - Trong `AdjustBalance(...)`, thêm `if (!this.EnsureAdmin(out var authError)) return authError;` ở đầu action (trước khi gửi `AdjustWalletBalanceCommand`).

---

## Phase 2 (P0/P1): Triển khai API còn thiếu & Luồng GHN / Địa chỉ mặc định / Đổi địa chỉ

### 2.1. CRUD Tài khoản ngân hàng đã lưu của Seller (`BankAccount`) & Tích hợp vào `RequestWithdrawal`
- **Hiện trạng:** `Wallet.Domain/Entities/BankAccount.cs` và `WalletDbContext.BankAccounts` (`UserId`, `BankName`, `AccountNumber`, `AccountHolderName`, `IsDefault`) đã có sẵn trong DB/Domain nhưng chưa có endpoint nào đọc/ghi; `RequestWithdrawalCommand` đang ép seller nhập tay `BankCode`, `AccountNumber`, `AccountHolderName` mỗi lần rút.
- **Các bước triển khai:**
  1. **Application Layer (`Wallet.Application/Features/BankAccounts/...`):**
     - Tạo Queries & Commands:
       - `ListMyBankAccountsQuery` (`GET`)
       - `CreateBankAccountCommand` (`BankName`, `AccountNumber`, `AccountHolderName`, `IsDefault`) — nếu `IsDefault == true` hoặc đây là tài khoản đầu tiên của user, tự động bỏ `IsDefault` ở các bản ghi cũ trong cùng transaction.
       - `SetDefaultBankAccountCommand(Guid Id)` — chỉ cho phép owner, đặt `IsDefault = true` và reset các tài khoản khác của `UserId` về `false`.
       - `DeleteBankAccountCommand(Guid Id)` — kiểm tra ownership; không cho xóa nếu đang được tham chiếu bởi `WithdrawalRequest` ở trạng thái `Pending`/`Approved`/`Processing` (hoặc snapshot thông tin ngân hàng lên `WithdrawalRequest` như hiện tại).
  2. **Tích hợp vào `RequestWithdrawalCommand` & `RequestWithdrawalCommandHandler`:**
     - Thêm `Guid? BankAccountId` vào `RequestWithdrawalCommand`.
     - Nếu có `BankAccountId` -> load từ `BankAccounts` theo `(Id, UserId)` (ném 404 nếu không thấy). Nếu không truyền `BankAccountId` và cũng không nhập tay -> fallback lấy bản ghi `IsDefault == true` của `UserId`.
     - Vẫn giữ snapshot `BankCode`/`AccountNumber`/`AccountHolderName` lên `WithdrawalRequest` và tính `WithdrawalWorkflow.Fingerprint(...)` trên giá trị đã resolve.
  3. **API Controller (`Wallet.Api/Controllers/Public/WalletsController.cs`):**
     - `GET /api/v1/wallets/bank-accounts` (`[RequireRole("seller")]`)
     - `POST /api/v1/wallets/bank-accounts` (`[RequireRole("seller")]`)
     - `PATCH /api/v1/wallets/bank-accounts/{id:guid}/default` (`[RequireRole("seller")]`)
     - `DELETE /api/v1/wallets/bank-accounts/{id:guid}` (`[RequireRole("seller")]`)

### 2.2. Seller tự hủy yêu cầu rút tiền đang `Pending` (`POST /api/v1/wallets/withdrawals/{id:guid}/cancel`)
- **Các bước triển khai:**
  1. **Domain (`Wallet.Domain/Enums/WithdrawalStatus.cs` & `AuditAction.cs`):**
     - Bổ sung `WithdrawalStatus.Cancelled` (nối vào cuối enum để không lệch giá trị số cũ).
     - Bổ sung giá trị `AuditAction` mới nếu cần ghi audit riêng (ví dụ nối cuối sau `WalletUnfrozen = 20`).
  2. **Application (`Wallet.Application/Features/Withdrawals/Commands/CancelWithdrawal/...`):**
     - Tạo `CancelWithdrawalCommand(Guid WithdrawalId, string UserId)`:
       - Lock `WithdrawalRequest` và `WalletAccount`; xác minh quyền sở hữu (`wallet.UserId == userId`).
       - Nếu `Status != WithdrawalStatus.Pending`, trả về `409 Conflict` (`WITHDRAWAL_INVALID_TRANSITION`).
       - Chuyển `Status = WithdrawalStatus.Cancelled`, gọi `wallet.ReleaseLockedFunds(amount + fee)`, ghi `AuditLog`.
     - Cập nhật `WithdrawalReader` / `WithdrawalWorkflow` nếu cần hiển thị trạng thái `Cancelled`.
  3. **API (`Wallet.Api/Controllers/Public/WalletsController.cs`):**
     - Thêm `POST /api/v1/wallets/withdrawals/{id:guid}/cancel` với `[RequireRole("seller")]`.

### 2.3. Chi tiết giao dịch ví theo ID (`GET /api/v1/wallets/transactions/{id:guid}`)
- **Các bước triển khai:**
  1. **Application (`Wallet.Application/Features/Wallets/Queries/GetTransaction/...`):**
     - Tạo `GetTransactionByIdQuery(Guid TransactionId, string UserId)`: truy vấn `WalletTransactions` join `WalletAccount` kiểm tra `UserId == callerUserId`. Trả `404` nếu không tồn tại hoặc không thuộc sở hữu.
     - Trả về đầy đủ `BalanceBefore`, `BalanceAfter`, `ReferenceId`, `Note`, loại giao dịch, trạng thái gateway.
  2. **API (`Wallet.Api/Controllers/Public/WalletsController.cs`):**
     - Thêm `GET /api/v1/wallets/transactions/{id:guid}` với `[RequireRole]`.

### 2.4. Stripe Direct Checkout: Resume, Cancel & Internal Expire
- **Các bước triển khai:**
  1. **Public Resume & Cancel (`WalletsController` — `[RequireRole]`):**
     - `POST /api/v1/wallets/checkout/direct/{orderId:guid}/resume`: tương tự `StripeTopupHistory.ResumeAsync`, tìm `DirectPaymentOrder` + `StripePaymentAttempt` (`AwaitingPayment`, có `CheckoutUrl`, chưa hết hạn) của buyer và trả về `CheckoutUrl` hiện có (không tạo trùng session). Trả `404`/`409` nếu không thể resume.
     - `POST /api/v1/wallets/checkout/direct/{orderId:guid}/cancel`: kiểm tra với Stripe rằng session chưa `paid`, đóng session/attempt về `Cancelled`/`Failed` để giải phóng `BlocksReplacement == true`, cho phép buyer chuyển sang `PayOrder` (trả bằng ví) mà không dính `409 PAYMENT_ORDER_CONFLICT`.
  2. **Internal Expire (`InternalWalletsController` — `[RequireInternalServiceKey]`):**
     - `POST /api/v1/internal/wallets/checkout/direct/{orderId:guid}/expire`: do `CommerceService` gọi khi đơn hàng quá hạn thanh toán (`Order expiry`), đánh dấu `DirectPaymentOrder` / `StripePaymentAttempt` hết hạn để chặn thanh toán muộn vào link Stripe cũ.

### 2.5. Admin/Staff: Tra cứu Auction Holds & Xử lý Stripe `ReconciliationRequired`
- **Các bước triển khai (`Wallet.Api/Controllers/Public/AdminWalletsController.cs`):**
  1. `GET /api/v1/admin/wallets/holds` và `GET /api/v1/admin/wallets/{userId:guid}/holds` (`[RequireRole("admin", "staff")]`): liệt kê các `AuctionHold` đang giữ tiền của user (giải thích trường hợp `AvailableBalance < Balance` khi CSKH hỗ trợ).
  2. `GET /api/v1/admin/wallets/stripe/reconciliation-required` (`this.EnsureAdmin`): liệt kê các `StripePaymentAttempt` bị đánh dấu `ReconciliationRequired` từ `ProcessStripeWebhookCommandHandler` / `ReconcilePendingTopupsCommandHandler`.
  3. `POST /api/v1/admin/wallets/stripe/attempts/{attemptId:guid}/resolve` (`this.EnsureAdmin`): cho phép Admin đối soát/đóng thủ công attempt bị treo kèm lý do và ghi `AuditLog`.

### 2.6. Luồng GHN Master Data, Địa chỉ mặc định & Đổi địa chỉ giao hàng
- **Phạm vi dữ liệu:** `WalletService` **không** lưu trữ GHN Master Data (Province/District/Ward) hay Address Book của Buyer (thuộc `UserService` và `FulfillmentService`). Không tạo API GHN hay Address Book tại `WalletService`.
- **Checkout bình thường (trước khi thanh toán):** `CommerceService` tính lại tổng tiền khi buyer đổi địa chỉ và chỉ gửi `Amount` cuối cùng sang `PayOrder` hoặc `CheckoutDirect` — **đã đáp ứng**.
- **Post-order Change-Address (sau khi đã thanh toán, trước khi bàn giao ĐVVC):**
  - *Lưu ý:* Nếu nghiệp vụ cho phép đổi địa chỉ sau khi đã thanh toán làm thay đổi phí ship, hiện `PayOrderWithWalletCommandHandler` và `RefundOrderPaymentCommandHandler` đang ràng buộc mỗi `OrderId` chỉ có 1 `PurchaseDebit` và 1 `RefundCredit` (`409 PAYMENT_ORDER_CONFLICT` / `REFUND_CONFLICT`). Khi có yêu cầu triển khai tính năng này (P2), xem mục 3.3 bên dưới.

---

## Phase 3 (P1/P2): Mở rộng vận hành & Hoàn thiện tính năng

### 3.1. Admin/Staff: Danh sách tất cả ví & Kích hoạt đối soát Stripe thủ công
- `GET /api/v1/admin/wallets` (`AdminWalletsController`, `[RequireRole("admin", "staff")]`): phân trang danh sách ví, hỗ trợ filter `status` (`ACTIVE`/`FROZEN`), `userId`, `search`.
- `POST /api/v1/admin/wallets/stripe/reconcile-pending` (`AdminWalletsController`, `this.EnsureAdmin`): cho phép Admin chủ động kích hoạt đối soát các phiên `Topup` và `DirectOrder` đang `Pending` (bù đắp cho việc gỡ `StripeTopupReconciliationWorker.cs`).

### 3.2. Chuẩn hóa route `holds` về namespace Internal (`/api/v1/internal/wallets/holds/...`)
- Hiện 6 endpoint S2S holds (`CreateHold`, `ReleaseHold`, `CaptureHold`, `RefundCapturedHold`, `GetHold`, `GetHoldByReference`) đang đặt trên `WalletsController` (`/api/v1/wallets/holds/...`) với `[RequireAuctionServiceKey]`, nên bị expose qua `wallets-route` ở Gateway.
- Tạo mirror trên `InternalWalletsController` (`/api/v1/internal/wallets/holds/...`) với `[RequireAuctionServiceKey]`, phối hợp `AuctionService` và `CommerceService` chuyển URL, sau đó gỡ route cũ trên `WalletsController`. *(Giữ nguyên `GET /api/v1/wallets/me/holds` trên `WalletsController` cho buyer).*

### 3.3. Hỗ trợ điều chỉnh tiền sau thanh toán khi đổi địa chỉ (`Post-order Change-Address`)
- Bổ sung `POST /api/v1/internal/wallets/orders/{orderId:guid}/adjust-payment` (`InternalWalletsController`, `[RequireInternalServiceKey]`, bắt buộc `Idempotency-Key`):
  - `DeltaAmount > 0`: kiểm tra `AvailableBalance` và `Status != "FROZEN"`, trừ thêm phần chênh lệch phí ship.
  - `DeltaAmount < 0`: hoàn phần chênh lệch phí ship vào ví buyer.

---

## Phụ thuộc liên service (Cross-Service Dependencies)

| Đối tác | Chiều phụ thuộc | Chi tiết phối hợp bắt buộc |
|---|---|---|
| **`CommerceService`** (`Commerce.Infrastructure/Services/Wallet/WalletClient.cs`) | **Chặn nhau (P0)** | Sau khi `WalletService` hoàn tất **Phase 1.1 & 1.2**, phải thông báo ngay cho `CommerceService` cập nhật `WalletClient.cs` chuyển 5 call sang `/api/v1/internal/wallets/...`:<br>1. `GetWalletBalanceAsync`: `GET /api/v1/internal/wallets/{userId}` (đọc `AvailableBalance`, `Status`).<br>2. `PayOrderAsync`: `POST /api/v1/internal/wallets/orders/{orderId}/payment`.<br>3. `SettleSellerSubOrderAsync`: `POST /api/v1/internal/wallets/sub-orders/{subOrderId}/settle`.<br>4. `RefundOrderPaymentAsync`: `POST /api/v1/internal/wallets/orders/{orderId}/refund`.<br>5. `RefundSubOrderPaymentAsync`: `POST /api/v1/internal/wallets/sub-orders/{subOrderId}/refund`.<br>Chỉ sau khi `CommerceService` deploy URL mới thì mới xóa hẳn 4 route S2S cũ trên `WalletsController`. |
| **`CommerceService`** (Order Expiry & Direct Checkout) | **Wallet làm trước (P1)** | Khi `WalletService` xong `POST /api/v1/internal/wallets/checkout/direct/{orderId:guid}/expire` (Phase 2.4), báo `CommerceService` gọi endpoint này trong job hết hạn đơn hàng. |
| **`AuctionService`** (`Auction.Infrastructure/Clients/WalletClient.cs`) | **Phối hợp P2** | Hiện các endpoint `/api/v1/wallets/holds/...` đã khớp contract. Khi triển khai Phase 3.2, báo `AuctionService` và `CommerceService` (`GetAuctionDepositHoldAsync`) chuyển sang `/api/v1/internal/wallets/holds/...`. |
| **`UserService`** (`User.Infrastructure/Services/External/WalletServiceClient.cs`) | **Đã khớp** | `POST /api/v1/internal/wallets/freeze-purchase` và `unfreeze-purchase` đã khớp hoàn toàn với `InternalWalletsController`; không cần thay đổi. |
| **`Gateway`** (`Gateway/Gateway/appsettings.json`) | **Kiểm tra cấu hình** | `wallets-route` (`/api/v1/wallets/{**catch-all}`) và `admin-wallets-route` (`/api/v1/admin/wallets/{**catch-all}`) tự động route các public endpoint mới ở Phase 2 & 3. Đảm bảo `/api/v1/internal/{**catch-all}` tiếp tục **không** được expose ra ngoài. |

---

## Checklist kiểm thử & Nghiệm thu (Verification & Definition of Done)

### Unit & Integration Tests cần bổ sung / cập nhật (`Wallet.Tests`)
1. **Bảo mật & Phân quyền (Phase 1):**
   - Test `InternalWalletsController`: từ chối `401` khi thiếu/sai `X-Internal-Service-Key`, chấp nhận `200` khi đúng key cho `GetUserWallet`, `PayOrder`, `SettleSellerOrder`, `RefundOrder`, `RefundSubOrder`.
   - Test `WalletsController`: xác nhận 4 route S2S cũ không còn truy cập công khai không cần auth.
   - Test `AdminWalletsController.AdjustBalance`: trả `403` khi role là `staff`, chỉ cho phép `admin`.
2. **Trạng thái `FROZEN` (Phase 1):**
   - Test `PayOrderWithWalletCommandHandler`, `TransferCommandHandler`, `RequestWithdrawalCommandHandler` trả `403 WALLET_FROZEN` khi `wallet.Status == "FROZEN"`.
3. **Tính năng mới (Phase 2 & 3):**
   - `BankAccountTests`: tạo, đặt mặc định (chỉ 1 `IsDefault = true`), xóa, chặn xóa khi đang gắn `WithdrawalRequest` đang mở, và `RequestWithdrawal` tự fallback về `BankAccount` mặc định.
   - `CancelWithdrawalTests`: hủy thành công khi `Pending` (nhả `LockedBalance`, ghi `AuditLog`), trả `409` khi đã `Approved`/`Processing`.
   - `GetTransactionByIdTests`: trả `200` cho chủ sở hữu, `404` khi truy cập giao dịch của user khác.
   - `DirectCheckoutLifecycleTests`: `resume` trả `CheckoutUrl` còn hạn không tạo trùng session; `cancel` giải phóng `BlocksReplacement` để `PayOrder` bằng ví thành công; `expire` chặn thanh toán muộn.
   - `AdminOperationsTests`: list ví, xem `holds`, list & resolve `ReconciliationRequired`.

### Lệnh kiểm tra bắt buộc trước khi tạo PR / Merge
```powershell
# Build toàn bộ solution
dotnet build WalletService/WalletService.sln --configuration Release

# Chạy toàn bộ unit/integration tests
dotnet test WalletService/Wallet.Tests/Wallet.Tests.csproj --verbosity normal

# Nếu có thêm/sửa entity hoặc enum cần migration (ví dụ WithdrawalStatus.Cancelled nếu map string/constraint):
dotnet ef migrations list --project WalletService/Wallet.Infrastructure --startup-project WalletService/Wallet.Api
```

### Tiêu chí nghiệm thu (Definition of Done)
- [ ] Đã rebase sạch lên `origin/dev`, không còn lẫn code `WalletHold` cũ từ `nexus-dev-be` (`6708602`).
- [ ] Toàn bộ 4 endpoint S2S của `CommerceService` đã chuyển sang `InternalWalletsController` với `[RequireInternalServiceKey]` và `CommerceService` đã cập nhật `WalletClient.cs`.
- [ ] `AdjustBalance` đã có `EnsureAdmin`; `PayOrder`, `Transfer`, `RequestWithdrawal` đều chặn ví `FROZEN` (`403 WALLET_FROZEN`).
- [ ] Hoàn thành các API P1 (`bank-accounts`, `withdrawals/{id}/cancel`, `transactions/{id}`, `checkout/direct/{orderId}/resume|cancel|expire`, `admin/wallets/holds`, `admin/wallets/stripe/reconciliation-required`).
- [ ] `dotnet build` và `dotnet test` pass 100% không có test bị skip.



---

## Bổ sung từ kiểm tra Config & `dotnet test`

- **Placeholder literal trong `Wallet.Api/appsettings.json`:** Các dòng `10-23` chứa `"WALLET_DB_CONNECTION"`, `"INTERNAL_SERVICE_SECRET"`, `"VNPAY_TMN_CODE"`, cần đảm bảo env/Development override khi chạy.
- **Thiếu `Stripe__*` trong `docker-compose.yml`:** Cần bổ sung đủ `Stripe__*` cho `wallet-service` trong `docker-compose.yml`.
- **Bật lại `InternalServiceAuthMiddleware`:** Kiểm tra `ServiceDefaultsExtensions.cs` để bật lại middleware bảo vệ `/api/v1/internal/*`.
- **Kết quả `dotnet test --no-restore` hiện tại:** **527 Passed**, 29 Skipped (Total: 556).
