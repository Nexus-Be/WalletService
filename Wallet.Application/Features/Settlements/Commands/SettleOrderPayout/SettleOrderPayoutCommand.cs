namespace Wallet.Application.Features.Settlements.Commands.SettleOrderPayout;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Wallet.Application.Common.Validation;

/// <summary>
/// Atomic seller payout (protocol V2). Commerce sends the complete allocation of one sub-order. Wallet moves the escrow(s),
/// credits the seller, records the platform fee and shipping allocation, and uses the reserved voucher subsidy, in one database
/// transaction. The identity OrderEscrowAmount + DepositEscrowAmount + SubsidyAmount = SellerReceiveAmount + PlatformFeeAmount +
/// ShippingAmount must hold: what leaves the escrows and the subsidy budget equals what is paid out or allocated.
/// </summary>
public sealed record SettleOrderPayoutCommand(
    [Required(ErrorMessage = "Business type is required.")]
    [RegularExpression("^(OrderPayout|AuctionPayout)$", ErrorMessage = "Business type must be OrderPayout or AuctionPayout.")]
    string BusinessType,
    Guid ParentOrderId,
    Guid SubOrderId,
    [Required(ErrorMessage = "Seller ID is required.")]
    [NonEmptyGuid(ErrorMessage = "Seller ID must be a valid UUID.")]
    string SellerId,
    [Range(0, double.MaxValue, ErrorMessage = "Seller receive amount must not be negative.")] [MoneyPrecision] decimal SellerReceiveAmount,
    [Range(0, double.MaxValue, ErrorMessage = "Platform fee amount must not be negative.")] [MoneyPrecision] decimal PlatformFeeAmount,
    [Range(0, double.MaxValue, ErrorMessage = "Shipping amount must not be negative.")] [MoneyPrecision] decimal ShippingAmount,
    [Range(0, double.MaxValue, ErrorMessage = "Subsidy amount must not be negative.")] [MoneyPrecision] decimal SubsidyAmount,
    [Range(0, double.MaxValue, ErrorMessage = "Order escrow amount must not be negative.")] [MoneyPrecision] decimal OrderEscrowAmount,
    [Range(0, double.MaxValue, ErrorMessage = "Deposit escrow amount must not be negative.")] [MoneyPrecision] decimal DepositEscrowAmount,
    [Range(1, 1000, ErrorMessage = "Payload version must be between 1 and 1000.")] int PayloadVersion = 1)
    : IRequest<SettleOrderPayoutResponse>, IRequestValidation
{
    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var parentError = RequestValidation.ValidateGuid(ParentOrderId, nameof(ParentOrderId), "Parent order ID");
        if (parentError is not null) yield return parentError;
        var subError = RequestValidation.ValidateGuid(SubOrderId, nameof(SubOrderId), "Sub-order ID");
        if (subError is not null) yield return subError;
    }

    /// <summary>
    /// SHA-256 of every field that decides where money goes. Two calls with the same key (business type, parent order, sub-order)
    /// and the same fingerprint are the same payout; a different fingerprint is a conflict, never a replay.
    /// </summary>
    public string Fingerprint()
    {
        static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        var canonical = string.Join('|',
            "v" + PayloadVersion.ToString(CultureInfo.InvariantCulture),
            BusinessType,
            ParentOrderId.ToString("D"),
            SubOrderId.ToString("D"),
            Guid.TryParse(SellerId, out var seller) ? seller.ToString("D") : SellerId,
            Money(SellerReceiveAmount), Money(PlatformFeeAmount), Money(ShippingAmount), Money(SubsidyAmount),
            Money(OrderEscrowAmount), Money(DepositEscrowAmount));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

public sealed record SettleOrderPayoutResponse(
    string SettlementId,
    string BusinessType,
    string ParentOrderId,
    string SubOrderId,
    string SellerWalletId,
    decimal SellerReceiveAmount,
    decimal PlatformFeeAmount,
    decimal ShippingAmount,
    decimal SubsidyAmount,
    decimal OrderEscrowAmount,
    decimal DepositEscrowAmount,
    string? EscrowReleaseTransactionId,
    string? SubsidyTransactionId,
    bool IsReplay,
    DateTimeOffset CreatedAt);
