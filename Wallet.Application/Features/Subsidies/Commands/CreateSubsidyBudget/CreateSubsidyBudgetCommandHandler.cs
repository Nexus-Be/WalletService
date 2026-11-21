namespace Wallet.Application.Features.Subsidies.Commands.CreateSubsidyBudget;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class CreateSubsidyBudgetCommandHandler : IRequestHandler<CreateSubsidyBudgetCommand, SubsidyBudgetResult>
{
    private readonly IWalletDbContext _context;

    public CreateSubsidyBudgetCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<SubsidyBudgetResult> Handle(CreateSubsidyBudgetCommand request, CancellationToken cancellationToken)
    {
        if (request.DailyLimit > request.TotalLimit || request.PerOrderCap > request.DailyLimit)
        {
            throw new BusinessRuleValidationException(
                "SUBSIDY_LIMITS_INVALID",
                "The per-order cap must not exceed the daily limit and the daily limit must not exceed the total limit.",
                400);
        }

        var now = DateTimeOffset.UtcNow;
        var budget = new PlatformSubsidyBudget
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            TotalLimit = request.TotalLimit,
            DailyLimit = request.DailyLimit,
            PerOrderCap = request.PerOrderCap,
            EvidenceRef = request.EvidenceRef.Trim(),
            Status = SubsidyBudgetStatus.PendingApproval,
            CreatedByAdminId = request.AdminUserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.PlatformSubsidyBudgets.Add(budget);
        _context.AddAuditLog(
            AuditAction.SubsidyBudgetCreate,
            nameof(PlatformSubsidyBudget),
            budget.Id.ToString(),
            newValues: new { budget.Name, budget.TotalLimit, budget.DailyLimit, budget.PerOrderCap, budget.EvidenceRef, Status = budget.Status.ToString() },
            userId: request.AdminUserId.ToString());
        await _context.SaveChangesAsync(cancellationToken);
        return budget.ToResult();
    }
}

internal static class SubsidyBudgetMapping
{
    public static SubsidyBudgetResult ToResult(this PlatformSubsidyBudget budget) => new(
        budget.Id.ToString(),
        budget.Name,
        budget.TotalLimit,
        budget.DailyLimit,
        budget.PerOrderCap,
        budget.EvidenceRef,
        budget.Status.ToString(),
        budget.CreatedByAdminId.ToString(),
        budget.ApprovedByAdminId?.ToString(),
        budget.ApprovedAt,
        budget.ReservedAmount,
        budget.UsedAmount,
        budget.TotalLimit - budget.ReservedAmount - budget.UsedAmount,
        budget.CreatedAt);
}
