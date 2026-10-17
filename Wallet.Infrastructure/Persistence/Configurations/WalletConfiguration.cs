namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public class WalletConfiguration : IEntityTypeConfiguration<WalletAccount>
{
    public void Configure(EntityTypeBuilder<WalletAccount> builder)
    {
        builder.ToTable("wallets");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.UserId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(w => w.UserId)
            .IsUnique();

        builder.Property(w => w.Balance)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(w => w.Currency)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("VND");

        builder.Property(w => w.Status)
            .IsRequired()
            .HasMaxLength(30)
            .HasDefaultValue("ACTIVE");

        builder.Property(w => w.CreatedAt)
            .IsRequired();

        builder.Property(w => w.UpdatedAt)
            .IsRequired();

        builder.HasMany(w => w.Transactions)
            .WithOne(t => t.Wallet)
            .HasForeignKey(t => t.WalletId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
