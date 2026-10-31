namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public class WalletTopupConfiguration : IEntityTypeConfiguration<WalletTopup>
{
    public void Configure(EntityTypeBuilder<WalletTopup> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.VnpTxnRef).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.VnpTxnRef).IsUnique();
        builder.Property(x => x.VnpTransactionNo).HasMaxLength(100);
        builder.Property(x => x.VnpBankCode).HasMaxLength(50);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.RawResponse).HasColumnType("jsonb");
        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasOne(t => t.Wallet)
            .WithMany(w => w.Topups)
            .HasForeignKey(t => t.WalletId);

        builder.Ignore(t => t.IpnLogs);
    }
}
