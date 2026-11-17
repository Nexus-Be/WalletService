namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public sealed class AuctionHoldConfiguration : IEntityTypeConfiguration<AuctionHold>
{
    public void Configure(EntityTypeBuilder<AuctionHold> builder)
    {
        builder.ToTable("AuctionHolds", table =>
        {
            table.HasCheckConstraint("CK_AuctionHolds_WholeAmount",
                "\"Amount\" > 0 AND \"Amount\" = trunc(\"Amount\")");
            table.HasCheckConstraint("CK_AuctionHolds_Status", "\"Status\" IN (0, 1, 2, 3)");
            table.HasCheckConstraint("CK_AuctionHolds_MetadataObject",
                "jsonb_typeof(\"MetadataJson\") = 'object'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.ReferenceType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReferenceId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId }).IsUnique();
        builder.HasIndex(x => new { x.OwnerUserId, x.CreatedAt });
        builder.HasOne<WalletAccount>().WithMany().HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.IsDeleted);
        foreach (var property in new[]
        {
            nameof(AuctionHold.WalletId), nameof(AuctionHold.OwnerUserId),
            nameof(AuctionHold.ReferenceType), nameof(AuctionHold.ReferenceId),
            nameof(AuctionHold.Amount), nameof(AuctionHold.MetadataJson)
        })
            builder.Property(property).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
