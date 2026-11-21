namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public sealed class PlatformSubsidyBudgetConfiguration : IEntityTypeConfiguration<PlatformSubsidyBudget>
{
    public void Configure(EntityTypeBuilder<PlatformSubsidyBudget> builder)
    {
        builder.ToTable("PlatformSubsidyBudgets", table =>
        {
            table.HasCheckConstraint("CK_PlatformSubsidyBudgets_Limits",
                "\"TotalLimit\" > 0 AND \"DailyLimit\" > 0 AND \"PerOrderCap\" > 0 AND \"DailyLimit\" <= \"TotalLimit\" AND \"PerOrderCap\" <= \"DailyLimit\"");
            table.HasCheckConstraint("CK_PlatformSubsidyBudgets_Usage",
                "\"ReservedAmount\" >= 0 AND \"UsedAmount\" >= 0 AND \"ReservedAmount\" + \"UsedAmount\" <= \"TotalLimit\"");
            table.HasCheckConstraint("CK_PlatformSubsidyBudgets_Approval",
                "(\"Status\" = 0 AND \"ApprovedByAdminId\" IS NULL) OR (\"Status\" IN (1, 2) AND \"ApprovedByAdminId\" IS NOT NULL AND \"ApprovedByAdminId\" <> \"CreatedByAdminId\")");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(150).IsRequired();
        builder.Property(item => item.EvidenceRef).HasMaxLength(500).IsRequired();
        foreach (var name in new[]
                 {
                     nameof(PlatformSubsidyBudget.TotalLimit), nameof(PlatformSubsidyBudget.DailyLimit), nameof(PlatformSubsidyBudget.PerOrderCap),
                     nameof(PlatformSubsidyBudget.ReservedAmount), nameof(PlatformSubsidyBudget.UsedAmount)
                 })
        {
            builder.Property(name).HasPrecision(18, 2);
        }

        builder.HasQueryFilter(item => !item.IsDeleted);
    }
}

public sealed class PlatformSubsidyReservationConfiguration : IEntityTypeConfiguration<PlatformSubsidyReservation>
{
    public void Configure(EntityTypeBuilder<PlatformSubsidyReservation> builder)
    {
        builder.ToTable("PlatformSubsidyReservations", table =>
        {
            table.HasCheckConstraint("CK_PlatformSubsidyReservations_Amount", "\"Amount\" > 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Amount).HasPrecision(18, 2);
        builder.Property(item => item.ReleaseReason).HasMaxLength(200);
        builder.HasIndex(item => item.SubOrderId).IsUnique();
        builder.HasIndex(item => new { item.BudgetId, item.CreatedAt });
        builder.HasOne<PlatformSubsidyBudget>().WithMany().HasForeignKey(item => item.BudgetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(item => !item.IsDeleted);
        foreach (var name in new[]
                 {
                     nameof(PlatformSubsidyReservation.BudgetId), nameof(PlatformSubsidyReservation.SubOrderId),
                     nameof(PlatformSubsidyReservation.OrderId), nameof(PlatformSubsidyReservation.Amount)
                 })
        {
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
