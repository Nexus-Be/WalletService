namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BankName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.AccountNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AccountHolderName).HasMaxLength(255).IsRequired();
    }
}
