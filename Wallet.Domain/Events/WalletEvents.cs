namespace Wallet.Domain.Events;

public interface IPaymentCompletedEvent
{
    string OrderId { get; }
    string TransactionId { get; }
    string Amount { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IPaymentFailedEvent
{
    string OrderId { get; }
    string Reason { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IStockReservedEvent
{
    string OrderId { get; }
    string ReservationId { get; }
    string UserId { get; }
    string TotalAmount { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IUserRegisteredEvent
{
    string UserId { get; }
}

public interface IOrderCheckoutRequestedEvent
{
    string OrderId { get; }
    string UserId { get; }
    string Amount { get; }
}

public interface IOrderCancelledEvent
{
    string OrderId { get; }
    string CancellationId { get; }
    string UserId { get; }
    string Amount { get; }
    string Reason { get; }
}

public interface IWalletTopupSucceededEvent
{
    string WalletId { get; }
    string TransactionId { get; }
    string Amount { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IWalletTopupInitiatedEvent
{
    string WalletId { get; }
    string TransactionReference { get; }
    string Amount { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IWalletTopupFailedEvent
{
    string WalletId { get; }
    string TransactionReference { get; }
    string Reason { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IWithdrawalRequestedEvent
{
    string WithdrawalId { get; }
    string WalletId { get; }
    string Amount { get; }
    string NetAmount { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IWithdrawalApprovedEvent
{
    string WithdrawalId { get; }
    string WalletId { get; }
    string Amount { get; }
    string NetAmount { get; }
    string ProcessedBy { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IWithdrawalRejectedEvent
{
    string WithdrawalId { get; }
    string WalletId { get; }
    string Reason { get; }
    string ProcessedBy { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IDirectPaymentInitiatedEvent
{
    string OrderId { get; }
    string TransactionId { get; }
    string Gateway { get; }
    string Amount { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IDirectPaymentFailedEvent
{
    string OrderId { get; }
    string Gateway { get; }
    string Reason { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IWalletDebitSucceededEvent
{
    string OrderId { get; }
    string TransactionId { get; }
    string Amount { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IWalletDebitFailedEvent
{
    string OrderId { get; }
    string Reason { get; }
    DateTimeOffset Timestamp { get; }
}

public interface IWalletRefundedEvent
{
    string OrderId { get; }
    string TransactionId { get; }
    string Amount { get; }
    DateTimeOffset Timestamp { get; }
}

public sealed record VnPayIpnResult(string ResponseCode, string Message);