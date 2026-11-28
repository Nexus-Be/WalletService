namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;

/// <summary>
/// Durable allocation of one sub-order payout (protocol V2). One row per (BusinessType, ParentOrderId, SubOrderId): it is both the
/// idempotency record and the proof of where each part of the escrow went. The platform fee and the shipping amount are recorded here
/// and do not change any wallet balance. The seller credit is a wallet transaction (EscrowRelease plus, when the platform funds part of
/// the voucher discount, PlatformSubsidyCredit).
/// </summary>
public class OrderSettlement : BaseEntity<Guid>
{
    public string BusinessType { get; init; } = string.Empty;
    public Guid ParentOrderId { get; init; }
    public Guid SubOrderId { get; init; }
    public Guid SellerId { get; init; }
    public Guid SellerWalletId { get; init; }
    public decimal SellerReceiveAmount { get; init; }
    public decimal PlatformFeeAmount { get; init; }
    public decimal ShippingAmount { get; init; }
    public decimal SubsidyAmount { get; init; }
    public decimal OrderEscrowAmount { get; init; }
    public decimal DepositEscrowAmount { get; init; }
    public string PayloadFingerprint { get; init; } = string.Empty;
    public int PayloadVersion { get; init; }
    public Guid? EscrowReleaseTransactionId { get; init; }
    public Guid? SubsidyTransactionId { get; init; }
}
