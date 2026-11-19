namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public class WithdrawalRequestConfiguration : IEntityTypeConfiguration<WithdrawalRequest>
{
    public void Configure(EntityTypeBuilder<WithdrawalRequest> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Fee).HasPrecision(18, 2);
        builder.Property(x => x.NetAmount).HasPrecision(18, 2);
        builder.Property(x => x.BankName).HasMaxLength(150);
        builder.Property(x => x.BankAccountNumber).HasMaxLength(100);
        builder.Property(x => x.BankAccountHolder).HasMaxLength(255);
        builder.Property(x => x.GatewayReferenceId).HasMaxLength(100);
        builder.Property(x => x.StatusReason).HasMaxLength(500);
        builder.Property(x => x.ClientIdempotencyKey).HasMaxLength(100);
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64);
        builder.HasIndex(x => x.GatewayReferenceId)
            .IsUnique()
            .HasFilter("\"GatewayReferenceId\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.WalletId, x.ClientIdempotencyKey })
            .IsUnique()
            .HasFilter("\"ClientIdempotencyKey\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => x.BankAccountId);
        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasOne(r => r.Wallet)
            .WithMany(w => w.WithdrawalRequests)
            .HasForeignKey(r => r.WalletId);
    }
}
