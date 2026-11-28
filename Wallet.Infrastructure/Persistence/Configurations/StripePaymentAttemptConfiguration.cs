namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public sealed class StripePaymentAttemptConfiguration : IEntityTypeConfiguration<StripePaymentAttempt>
{
    public void Configure(EntityTypeBuilder<StripePaymentAttempt> builder)
    {
        builder.ToTable("StripePaymentAttempts", table =>
        {
            table.HasCheckConstraint("CK_StripePaymentAttempts_WholeAmount",
                "\"Amount\" > 0 AND \"Amount\" = trunc(\"Amount\")");
            table.HasCheckConstraint("CK_StripePaymentAttempts_Purpose",
                "(\"Purpose\" = 0 AND \"WalletTopupId\" IS NOT NULL AND \"DirectPaymentOrderId\" IS NULL) OR " +
                "(\"Purpose\" = 1 AND \"WalletTopupId\" IS NULL AND \"DirectPaymentOrderId\" IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProviderIdempotencyKey).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ClientIdempotencyKey).HasMaxLength(128);
        foreach (var property in new[]
        {
            nameof(StripePaymentAttempt.Purpose),
            nameof(StripePaymentAttempt.OwnerId),
            nameof(StripePaymentAttempt.WalletTopupId),
            nameof(StripePaymentAttempt.DirectPaymentOrderId),
            nameof(StripePaymentAttempt.Amount),
            nameof(StripePaymentAttempt.RequestFingerprint),
            nameof(StripePaymentAttempt.ProviderIdempotencyKey),
            nameof(StripePaymentAttempt.ClientIdempotencyKey)
        })
        {
            builder.Property(property).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
        builder.Property(x => x.CheckoutSessionId).HasMaxLength(255);
        builder.Property(x => x.PaymentIntentId).HasMaxLength(255);
        builder.Property(x => x.CheckoutUrl).HasMaxLength(2048);
        builder.HasIndex(x => x.ProviderIdempotencyKey).IsUnique();
        builder.HasIndex(x => x.CheckoutSessionId).IsUnique();
        builder.HasIndex(x => x.PaymentIntentId).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.ClientIdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.WalletTopupId).IsUnique()
            .HasFilter("\"BlocksReplacement\" = TRUE AND \"WalletTopupId\" IS NOT NULL");
        builder.HasIndex(x => x.DirectPaymentOrderId).IsUnique()
            .HasFilter("\"BlocksReplacement\" = TRUE AND \"DirectPaymentOrderId\" IS NOT NULL");
        builder.HasOne<WalletTopup>().WithMany().HasForeignKey(x => x.WalletTopupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DirectPaymentOrder>().WithMany().HasForeignKey(x => x.DirectPaymentOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
