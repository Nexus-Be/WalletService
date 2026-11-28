namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public sealed class AuctionHoldCaptureConfiguration : IEntityTypeConfiguration<AuctionHoldCapture>
{
    public void Configure(EntityTypeBuilder<AuctionHoldCapture> builder)
    {
        builder.ToTable("AuctionHoldCaptures", table => table.HasCheckConstraint("CK_AuctionHoldCaptures_Purpose",
            "(\"Purpose\" = 'FORFEIT' AND \"OrderId\" IS NULL) OR " +
            "(\"Purpose\" = 'ORDER_PAYMENT' AND \"OrderId\" IS NOT NULL)"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(item => item.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.HasIndex(item => item.HoldId).IsUnique();
        builder.HasOne<AuctionHold>().WithMany().HasForeignKey(item => item.HoldId).OnDelete(DeleteBehavior.Restrict);
        foreach (var name in new[] { nameof(AuctionHoldCapture.HoldId), nameof(AuctionHoldCapture.Purpose),
                     nameof(AuctionHoldCapture.OrderId), nameof(AuctionHoldCapture.RequestFingerprint) })
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class AuctionCaptureTransferConfiguration : IEntityTypeConfiguration<AuctionCaptureTransfer>
{
    public void Configure(EntityTypeBuilder<AuctionCaptureTransfer> builder)
    {
        builder.ToTable("AuctionCaptureTransfers", table =>
        {
            table.HasCheckConstraint("CK_AuctionCaptureTransfers_Amount",
                "\"Amount\" > 0 AND \"Amount\" = trunc(\"Amount\")");
            table.HasCheckConstraint("CK_AuctionCaptureTransfers_Recipient",
                "(\"RecipientType\" = 'SELLER_WALLET' AND \"RecipientUserId\" IS NOT NULL AND \"WalletTransactionId\" IS NOT NULL) OR " +
                "(\"RecipientType\" IN ('PLATFORM', 'SYSTEM_ESCROW') AND \"RecipientUserId\" IS NULL AND \"WalletTransactionId\" IS NULL)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.RecipientType).HasMaxLength(32).IsRequired();
        builder.Property(item => item.Amount).HasPrecision(18, 2);
        builder.HasOne<AuctionHoldCapture>().WithMany().HasForeignKey(item => item.CaptureId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WalletTransaction>().WithMany().HasForeignKey(item => item.WalletTransactionId).OnDelete(DeleteBehavior.Restrict);
        foreach (var name in new[] { nameof(AuctionCaptureTransfer.CaptureId), nameof(AuctionCaptureTransfer.RecipientType),
                     nameof(AuctionCaptureTransfer.RecipientUserId), nameof(AuctionCaptureTransfer.Amount),
                     nameof(AuctionCaptureTransfer.WalletTransactionId) })
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class AuctionDepositEscrowConfiguration : IEntityTypeConfiguration<AuctionDepositEscrow>
{
    public void Configure(EntityTypeBuilder<AuctionDepositEscrow> builder)
    {
        builder.ToTable("AuctionDepositEscrows", table => table.HasCheckConstraint("CK_AuctionDepositEscrows_Balance",
            "\"OriginalAmount\" > 0 AND \"OriginalAmount\" = trunc(\"OriginalAmount\") AND " +
            "\"RemainingAmount\" >= 0 AND \"RemainingAmount\" <= \"OriginalAmount\" AND \"RemainingAmount\" = trunc(\"RemainingAmount\")"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.OriginalAmount).HasPrecision(18, 2);
        builder.Property(item => item.RemainingAmount).HasPrecision(18, 2);
        builder.HasIndex(item => item.HoldId).IsUnique();
        builder.HasIndex(item => item.OrderId);
        builder.HasOne<AuctionHold>().WithMany().HasForeignKey(item => item.HoldId).OnDelete(DeleteBehavior.Restrict);
        foreach (var name in new[] { nameof(AuctionDepositEscrow.HoldId), nameof(AuctionDepositEscrow.OrderId),
                     nameof(AuctionDepositEscrow.OriginalAmount) })
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
