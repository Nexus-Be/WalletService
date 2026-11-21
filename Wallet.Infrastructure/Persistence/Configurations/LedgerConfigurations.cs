namespace Wallet.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

public sealed class LedgerSettingsConfiguration : IEntityTypeConfiguration<LedgerSettings>
{
    public void Configure(EntityTypeBuilder<LedgerSettings> builder)
    {
        builder.ToTable("LedgerSettings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
    }
}

public sealed class LedgerPostingPendingConfiguration : IEntityTypeConfiguration<LedgerPostingPending>
{
    public void Configure(EntityTypeBuilder<LedgerPostingPending> builder)
    {
        builder.ToTable("LedgerPostingPending");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Delta).HasPrecision(18, 2);
        builder.Property(x => x.WalletBalanceAfter).HasPrecision(18, 2);
        builder.Property(x => x.OpeningBalance).HasPrecision(18, 2);
        builder.Property(x => x.RelatedTransactionIds).HasMaxLength(2000);
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.HasIndex(x => new { x.WalletId, x.LedgerEpoch, x.LedgerSeq }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}

public sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable("LedgerAccounts");
        builder.HasKey(x => x.Code);
        builder.Property(x => x.Code).HasMaxLength(60);
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
    }
}

public sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("JournalEntries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(300);
        builder.HasIndex(x => x.WalletId);
        builder.HasIndex(x => x.PendingId).IsUnique().HasFilter("\"PendingId\" IS NOT NULL");
        builder.HasIndex(x => x.WalletTransactionId);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.EntryId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        builder.ToTable("JournalLines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AccountCode).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Debit).HasPrecision(18, 2);
        builder.Property(x => x.Credit).HasPrecision(18, 2);
        builder.HasIndex(x => x.EntryId);
        builder.HasIndex(x => x.AccountCode);
    }
}

public sealed class ReconciliationRunConfiguration : IEntityTypeConfiguration<ReconciliationRun>
{
    public void Configure(EntityTypeBuilder<ReconciliationRun> builder)
    {
        builder.ToTable("ReconciliationRuns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Invariants).HasMaxLength(200);
        builder.Property(x => x.SkippedNote).HasMaxLength(500);
        builder.Property(x => x.UnavailableSources).HasMaxLength(300);
        builder.HasIndex(x => x.CreatedAt);
    }
}

public sealed class ReconciliationFindingConfiguration : IEntityTypeConfiguration<ReconciliationFinding>
{
    public void Configure(EntityTypeBuilder<ReconciliationFinding> builder)
    {
        builder.ToTable("ReconciliationFindings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.InvariantCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReferenceId).HasMaxLength(100);
        builder.Property(x => x.Source).HasMaxLength(60);
        builder.Property(x => x.ExpectedValue).HasMaxLength(200);
        builder.Property(x => x.ActualValue).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Note).HasMaxLength(2000);
        builder.Property(x => x.DedupKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.DedupKey).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}
