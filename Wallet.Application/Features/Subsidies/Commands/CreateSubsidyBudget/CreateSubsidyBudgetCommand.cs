namespace Wallet.Application.Features.Subsidies.Commands.CreateSubsidyBudget;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

/// <summary>
/// Proposes a platform subsidy budget. It stays pending and unusable until a different admin approves it. The evidence reference
/// points to the proof of the funding source (for example a bank transfer or a signed approval).
/// </summary>
public sealed record CreateSubsidyBudgetCommand(
    [Required(ErrorMessage = "Name is required.")] [StringLength(150, ErrorMessage = "Name must be 150 characters or fewer.")] string Name,
    [Range(0.01, double.MaxValue, ErrorMessage = "Total limit must be greater than zero.")] [MoneyPrecision] decimal TotalLimit,
    [Range(0.01, double.MaxValue, ErrorMessage = "Daily limit must be greater than zero.")] [MoneyPrecision] decimal DailyLimit,
    [Range(0.01, double.MaxValue, ErrorMessage = "Per-order cap must be greater than zero.")] [MoneyPrecision] decimal PerOrderCap,
    [Required(ErrorMessage = "Evidence reference is required.")] [StringLength(500, ErrorMessage = "Evidence reference must be 500 characters or fewer.")] string EvidenceRef)
    : IRequest<SubsidyBudgetResult>
{
    [JsonIgnore] public Guid AdminUserId { get; init; }
}

public sealed record SubsidyBudgetResult(
    string Id,
    string Name,
    decimal TotalLimit,
    decimal DailyLimit,
    decimal PerOrderCap,
    string EvidenceRef,
    string Status,
    string CreatedByAdminId,
    string? ApprovedByAdminId,
    DateTimeOffset? ApprovedAt,
    decimal ReservedAmount,
    decimal UsedAmount,
    decimal AvailableAmount,
    DateTimeOffset CreatedAt);
