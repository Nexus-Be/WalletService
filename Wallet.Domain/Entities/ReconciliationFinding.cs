namespace Wallet.Domain.Entities;

using Wallet.Domain.Enums;

public class ReconciliationFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    /// <summary>The invariant, for example I2.</summary>
    public string InvariantCode { get; set; } = string.Empty;
    /// <summary>What was wrong, for example I2_MISSING_WALLET_DEBIT.</summary>
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "High";
    public ReconciliationFindingStatus Status { get; set; } = ReconciliationFindingStatus.Open;
    public string? ReferenceId { get; set; }
    public string? Source { get; set; }
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public string? Description { get; set; }
    public string? Note { get; set; }
    /// <summary>(InvariantCode, ReferenceId, Code): the same problem found again is not stored twice.</summary>
    public string DedupKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
}
