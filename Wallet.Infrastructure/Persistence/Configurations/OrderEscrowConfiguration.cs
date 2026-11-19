namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public sealed class OrderEscrowConfiguration : IEntityTypeConfiguration<OrderEscrow>
{
    public void Configure(EntityTypeBuilder<OrderEscrow> builder)
    {
        builder.ToTable("OrderEscrows", table => table.HasCheckConstraint(
            "CK_OrderEscrows_Balance",
            "\"OriginalAmount\" > 0 AND \"RemainingAmount\" >= 0 AND \"RemainingAmount\" <= \"OriginalAmount\""));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.OriginalAmount).HasPrecision(18, 2);
        builder.Property(item => item.RemainingAmount).HasPrecision(18, 2);
        builder.HasIndex(item => item.OrderId).IsUnique();
        builder.HasOne<WalletAccount>()
            .WithMany()
            .HasForeignKey(item => item.WalletId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(item => !item.IsDeleted);

        foreach (var name in new[]
                 {
                     nameof(OrderEscrow.OrderId),
                     nameof(OrderEscrow.WalletId)
                 })
        {
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
