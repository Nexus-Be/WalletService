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