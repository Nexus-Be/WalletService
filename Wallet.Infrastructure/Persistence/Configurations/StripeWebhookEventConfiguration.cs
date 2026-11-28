namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public sealed class StripeWebhookEventConfiguration : IEntityTypeConfiguration<StripeWebhookEvent>
{
    public void Configure(EntityTypeBuilder<StripeWebhookEvent> builder)
    {
        builder.ToTable("StripeWebhookEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderEventId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.ProviderEventId).IsUnique();
        builder.HasOne<StripePaymentAttempt>().WithMany().HasForeignKey(x => x.PaymentAttemptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
