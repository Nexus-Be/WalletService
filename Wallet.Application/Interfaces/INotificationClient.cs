namespace Wallet.Application.Interfaces;

public sealed record NotificationDispatchResult(
    bool Succeeded,
    int? StatusCode = null,
    string? NotificationId = null,
    string? Error = null);

public sealed record WithdrawalNotificationEvent(
    string SourceEventId,
    Guid WithdrawalId,
    Guid RecipientUserId,
    string Status,
    DateTimeOffset ConfirmedAt,
    string? DisplayReason,
    string? TransactionId,
    string? TraceId);

public sealed record AuctionDepositNotificationEvent(
    string SourceEventId,
    string TransactionId,
    string? AuctionId,
    Guid BidderId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset ConfirmedAt,
    string? TraceId);

public sealed record WalletTopupNotificationEvent(
    string SourceEventId,
    Guid TopupId,
    Guid TransactionId,
    Guid RecipientUserId,
    long Amount,
    string Currency,
    DateTimeOffset ConfirmedAt,
    string? TraceId);

public interface INotificationClient
{
    Task<NotificationDispatchResult> SendWithdrawalEventAsync(
        WithdrawalNotificationEvent payload,
        CancellationToken cancellationToken = default);

    Task<NotificationDispatchResult> SendAuctionDepositEventAsync(
        AuctionDepositNotificationEvent payload,
        CancellationToken cancellationToken = default);

    Task<NotificationDispatchResult> SendWalletTopupEventAsync(
        WalletTopupNotificationEvent payload,
        CancellationToken cancellationToken = default);
}
