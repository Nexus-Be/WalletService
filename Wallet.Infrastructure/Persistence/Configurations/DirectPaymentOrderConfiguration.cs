namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public class DirectPaymentOrderConfiguration : IEntityTypeConfiguration<DirectPaymentOrder>
{
    public void Configure(EntityTypeBuilder<DirectPaymentOrder> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderId).IsRequired();
        builder.HasIndex(x => x.OrderId);

        builder.Property(x => x.VnpTxnRef).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.VnpTxnRef).IsUnique();

        builder.Property(x => x.VnpTransactionNo).HasMaxLength(100);
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.RawResponse).HasColumnType("jsonb");

        builder.HasMany(d => d.IpnLogs)
            .WithOne(l => l.DirectPaymentOrder)
            .HasForeignKey(l => l.VnpTxnRef)
            .HasPrincipalKey(d => d.VnpTxnRef);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
