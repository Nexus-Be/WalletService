namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;
using Wallet.Domain.Enums;

/// <summary>One reservation per sub-order (unique), so a repeated hold, release or use cannot count twice.</summary>
public class PlatformSubsidyReservation : BaseEntity<Guid>
{
    public Guid BudgetId { get; init; }
    public Guid SubOrderId { get; init; }
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public SubsidyReservationStatus Status { get; set; } = SubsidyReservationStatus.Held;
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string? ReleaseReason { get; set; }
}
