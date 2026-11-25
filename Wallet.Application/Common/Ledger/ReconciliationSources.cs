namespace Wallet.Application.Common.Ledger;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed record SubOrderRecon(
    Guid SubOrderId, string Status, string PaymentMethod, decimal ItemsAmount, decimal ProductDiscountAmount, decimal ShopDiscountAmount,
    decimal SystemDiscountAmount, decimal ShippingFeeAmount, decimal TotalAmount, decimal SellerReceiveAmount, decimal PlatformFeeAmount,
    bool IsEscrowSettled, DateTimeOffset? PaidAt);

public sealed record RefundRecon(Guid RefundId, Guid? SubOrderId, Guid? OrderId, decimal Amount, string Status, Guid? WalletTransactionId, Guid? RefundTransactionId);

public sealed record OrderRecon(Guid OrderId, Guid BuyerId, string Status, decimal TotalAmount, DateTimeOffset UpdatedAt, IReadOnlyList<SubOrderRecon> SubOrders, IReadOnlyList<RefundRecon> Refunds);

public sealed record DepositRecon(Guid DepositId, Guid AuctionId, Guid BidderId, decimal Amount, string Status, string? HoldId, DateTimeOffset UpdatedAt);

/// <summary>Reads order and deposit data from the other services. Throws when a service cannot be reached; the run then reports a partial result.</summary>
public interface IReconciliationSourceClient
{
    Task<IReadOnlyList<OrderRecon>> GetOrdersAsync(DateTimeOffset? updatedFrom, CancellationToken cancellationToken);

    Task<IReadOnlyList<DepositRecon>> GetDepositsAsync(DateTimeOffset? updatedFrom, CancellationToken cancellationToken);
}
