namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public sealed class AuctionHoldOperationConfiguration : IEntityTypeConfiguration<AuctionHoldOperation>
{
    public void Configure(EntityTypeBuilder<AuctionHoldOperation> builder)
    {
        builder.ToTable("AuctionHoldOperations", table =>
        {
            table.HasCheckConstraint("CK_AuctionHoldOperations_ResponseObject",
                "jsonb_typeof(\"ResponseJson\") = 'object'");
            table.HasCheckConstraint("CK_AuctionHoldOperations_HttpStatus", "\"HttpStatus\" IN (200, 201)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Operation).HasMaxLength(32).IsRequired();
        builder.Property(item => item.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ResponseJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(item => item.IdempotencyKey).IsUnique();
        builder.HasOne<AuctionHold>().WithMany().HasForeignKey(item => item.HoldId)
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in new[]
        {
            nameof(AuctionHoldOperation.HoldId), nameof(AuctionHoldOperation.IdempotencyKey),
            nameof(AuctionHoldOperation.Operation), nameof(AuctionHoldOperation.RequestFingerprint),
            nameof(AuctionHoldOperation.ResponseJson), nameof(AuctionHoldOperation.HttpStatus)
        })
            builder.Property(property).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
