using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

namespace Wallet.Infrastructure.Persistence.Configurations;

public class WalletAccountConfiguration : IEntityTypeConfiguration<WalletAccount>
{
    public void Configure(EntityTypeBuilder<WalletAccount> builder)
    {
        builder.HasKey(w => w.Id);
        builder.HasIndex(w => w.UserId).IsUnique();
        builder.Property(w => w.Balance).HasPrecision(18, 2);
        builder.Property(w => w.LockedBalance).HasPrecision(18, 2);
        builder.Property(w => w.Status).HasMaxLength(30);

        builder.HasMany(w => w.Transactions)
            .WithOne()
            .HasForeignKey(t => t.WalletId);

        builder.HasMany(w => w.Topups)
            .WithOne()
            .HasForeignKey(t => t.WalletId);

        builder.HasMany(w => w.WithdrawalRequests)
            .WithOne()
            .HasForeignKey(r => r.WalletId);
    }
}