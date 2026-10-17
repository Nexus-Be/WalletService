namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("bank_accounts");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.UserId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(b => b.UserId);

        builder.Property(b => b.BankName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(b => b.AccountNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(b => b.AccountHolderName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(b => b.CreatedAt)
            .IsRequired();
    }
}
