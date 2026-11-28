namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;
using Wallet.Domain.Enums;

/// <summary>
/// Budget that pays the platform-funded part of voucher discounts. A budget is created by one admin and only becomes usable after a
/// different admin approves it with a proof of the funding source. Without an approved budget no subsidy can be reserved.
/// </summary>
public class PlatformSubsidyBudget : BaseEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public decimal TotalLimit { get; set; }
    public decimal DailyLimit { get; set; }
    public decimal PerOrderCap { get; set; }
    public string EvidenceRef { get; set; } = string.Empty;
    public SubsidyBudgetStatus Status { get; set; } = SubsidyBudgetStatus.PendingApproval;
    public Guid CreatedByAdminId { get; set; }
    public Guid? ApprovedByAdminId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public decimal ReservedAmount { get; set; }
    public decimal UsedAmount { get; set; }
}
