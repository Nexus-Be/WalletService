namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;

/// <summary>The wallet that holds the refund fund. It is an ordinary wallet of a fixed system user; nobody logs in as it.</summary>
public static class RefundFundAccount
{
    public static readonly Guid UserId = new("f0f0f0f0-0000-0000-0000-000000000001");
}

/// <summary>
/// One refund of a buyer after the order was paid out to the seller, paid from the refund fund. One row per sub-order (unique): it is the
/// idempotency record and the proof of the approvals, of what was taken back from the seller and of what is still missing. Only refunds
/// that really moved money are stored; a case that could not be paid (fund empty, cap exceeded) is answered to Commerce, which keeps it in
/// its NeedsFinance queue.
/// </summary>
public class RefundFundPayout : BaseEntity<Guid>
{
    public Guid SubOrderId { get; set; }
    public Guid ParentOrderId { get; set; }
    public Guid BuyerId { get; set; }
    public Guid SellerId { get; set; }
    public decimal Amount { get; set; }
    public decimal SellerRecoveryTarget { get; set; }
    public decimal SellerRecoveredAmount { get; set; }
    public decimal SellerShortfall { get; set; }
    public Guid FirstApproverId { get; set; }
    public Guid? SecondApproverId { get; set; }
    public Guid BuyerTransactionId { get; set; }
    public Guid FundTransactionId { get; set; }
    public Guid? RecoveryTransactionId { get; set; }
}
