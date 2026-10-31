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
        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasOne(r => r.Wallet)
            .WithMany(w => w.WithdrawalRequests)
            .HasForeignKey(r => r.WalletId);
    }
}
