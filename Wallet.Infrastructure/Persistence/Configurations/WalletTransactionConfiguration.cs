namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.BalanceBefore).HasPrecision(18, 2);
        builder.Property(t => t.BalanceAfter).HasPrecision(18, 2);
        builder.Property(t => t.ClientIdempotencyKey).HasMaxLength(128);
        builder.Property(t => t.RequestFingerprint).HasMaxLength(64);
        builder.HasIndex(t => new { t.UserId, t.CreatedAt });
        builder.HasIndex(t => new { t.ReferenceId, t.Type });
        builder.HasIndex(t => new { t.ReferenceId, t.ClientIdempotencyKey })
            .IsUnique()
            .HasFilter("\"ClientIdempotencyKey\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        builder.HasQueryFilter(t => !t.IsDeleted);

        builder.HasOne(t => t.Wallet)
            .WithMany(w => w.Transactions)
            .HasForeignKey(t => t.WalletId);
    }
}