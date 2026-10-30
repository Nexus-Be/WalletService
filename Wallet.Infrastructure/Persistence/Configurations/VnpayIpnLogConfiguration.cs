namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public class VnpayIpnLogConfiguration : IEntityTypeConfiguration<VnpayIpnLog>
{
    public void Configure(EntityTypeBuilder<VnpayIpnLog> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.VnpTxnRef).HasMaxLength(100).IsRequired();
        builder.Property(x => x.VnpResponseCode).HasMaxLength(50);
        builder.Property(x => x.VnpTransactionNo).HasMaxLength(100);
        builder.Property(x => x.ClientIp).HasMaxLength(50);
        builder.Property(x => x.RawPayload).HasColumnType("jsonb");

        builder.HasIndex(x => x.VnpTxnRef);

        builder.Ignore(x => x.WalletTopup);
        builder.Ignore(x => x.DirectPaymentOrder);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
