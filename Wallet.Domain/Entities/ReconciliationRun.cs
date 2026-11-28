namespace Wallet.Domain.Entities;

using Wallet.Domain.Enums;

public class ReconciliationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }
    public ReconciliationRunStatus Status { get; set; } = ReconciliationRunStatus.Completed;
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    /// <summary>Comma separated invariant codes that were asked for (empty means all).</summary>
    public string? Invariants { get; set; }
    /// <summary>Invariants that were not evaluated, with the reason, for example "I10,I11: shadow ledger is off".</summary>
    public string? SkippedNote { get; set; }
    public string? UnavailableSources { get; set; }
    public int FindingCount { get; set; }
}
