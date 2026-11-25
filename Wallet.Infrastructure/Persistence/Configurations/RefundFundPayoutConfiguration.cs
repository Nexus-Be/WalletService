namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public sealed class RefundFundPayoutConfiguration : IEntityTypeConfiguration<RefundFundPayout>
{
    public void Configure(EntityTypeBuilder<RefundFundPayout> builder)
    {
        builder.ToTable("RefundFundPayouts", table =>
        {
            table.HasCheckConstraint("CK_RefundFundPayouts_Amounts",
                "\"Amount\" > 0 AND \"SellerRecoveryTarget\" >= 0 AND \"SellerRecoveredAmount\" >= 0 AND \"SellerShortfall\" >= 0 " +
                "AND \"SellerRecoveredAmount\" <= \"SellerRecoveryTarget\" AND \"SellerRecoveredAmount\" + \"SellerShortfall\" = \"SellerRecoveryTarget\"");
        });
        builder.HasKey(item => item.Id);
        foreach (var name in new[]
                 {
                     nameof(RefundFundPayout.Amount), nameof(RefundFundPayout.SellerRecoveryTarget),
                     nameof(RefundFundPayout.SellerRecoveredAmount), nameof(RefundFundPayout.SellerShortfall)
                 })
        {
            builder.Property(name).HasPrecision(18, 2);
        }

        // One refund per sub-order, whatever report or admin asked for it.
        builder.HasIndex(item => item.SubOrderId).IsUnique();
        builder.HasIndex(item => item.CreatedAt);
        builder.HasQueryFilter(item => !item.IsDeleted);

        // A payout is evidence: once written, nothing about it changes.
        foreach (var name in new[]
                 {
                     nameof(RefundFundPayout.SubOrderId), nameof(RefundFundPayout.ParentOrderId), nameof(RefundFundPayout.BuyerId),
                     nameof(RefundFundPayout.SellerId), nameof(RefundFundPayout.Amount), nameof(RefundFundPayout.SellerRecoveryTarget),
                     nameof(RefundFundPayout.SellerRecoveredAmount), nameof(RefundFundPayout.SellerShortfall)
                 })
        {
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
