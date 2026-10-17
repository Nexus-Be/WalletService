namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("wallet_transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.WalletId)
            .IsRequired();

        builder.Property(t => t.UserId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(t => t.UserId);
        builder.HasIndex(t => t.CreatedAt);

        builder.Property(t => t.Type)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(t => t.Currency)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("VND");

        builder.Property(t => t.BalanceAfter)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(t => t.Status)
            .IsRequired()
            .HasMaxLength(30)
            .HasDefaultValue("SUCCESS");

        builder.Property(t => t.Note)
            .HasMaxLength(500);

        builder.Property(t => t.CreatedAt)
            .IsRequired();
    }
}
