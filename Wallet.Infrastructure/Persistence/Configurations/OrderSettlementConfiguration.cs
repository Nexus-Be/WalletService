namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public sealed class OrderSettlementConfiguration : IEntityTypeConfiguration<OrderSettlement>
{
    public void Configure(EntityTypeBuilder<OrderSettlement> builder)
    {
        builder.ToTable("OrderSettlements", table =>
        {
            table.HasCheckConstraint("CK_OrderSettlements_Amounts",
                "\"SellerReceiveAmount\" >= 0 AND \"PlatformFeeAmount\" >= 0 AND \"ShippingAmount\" >= 0 AND \"SubsidyAmount\" >= 0 " +
                "AND \"OrderEscrowAmount\" >= 0 AND \"DepositEscrowAmount\" >= 0");
            table.HasCheckConstraint("CK_OrderSettlements_Identity",
                "\"OrderEscrowAmount\" + \"DepositEscrowAmount\" + \"SubsidyAmount\" = \"SellerReceiveAmount\" + \"PlatformFeeAmount\" + \"ShippingAmount\"");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.BusinessType).HasMaxLength(30).IsRequired();
        builder.Property(item => item.PayloadFingerprint).HasMaxLength(64).IsRequired();
        foreach (var name in new[]
                 {
                     nameof(OrderSettlement.SellerReceiveAmount), nameof(OrderSettlement.PlatformFeeAmount),
                     nameof(OrderSettlement.ShippingAmount), nameof(OrderSettlement.SubsidyAmount),
                     nameof(OrderSettlement.OrderEscrowAmount), nameof(OrderSettlement.DepositEscrowAmount)
                 })
        {
            builder.Property(name).HasPrecision(18, 2);
        }

        builder.HasIndex(item => new { item.BusinessType, item.ParentOrderId, item.SubOrderId }).IsUnique();
        builder.HasIndex(item => item.SubOrderId);
        builder.HasQueryFilter(item => !item.IsDeleted);

        // An allocation is evidence: once written, none of its figures may change.
        foreach (var name in new[]
                 {
                     nameof(OrderSettlement.BusinessType), nameof(OrderSettlement.ParentOrderId), nameof(OrderSettlement.SubOrderId),
                     nameof(OrderSettlement.SellerId), nameof(OrderSettlement.SellerWalletId),
                     nameof(OrderSettlement.SellerReceiveAmount), nameof(OrderSettlement.PlatformFeeAmount),
                     nameof(OrderSettlement.ShippingAmount), nameof(OrderSettlement.SubsidyAmount),
                     nameof(OrderSettlement.OrderEscrowAmount), nameof(OrderSettlement.DepositEscrowAmount),
                     nameof(OrderSettlement.PayloadFingerprint)
                 })
        {
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
