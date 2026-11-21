namespace Wallet.Application.Features.Subsidies.Commands.ApproveSubsidyBudget;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.Subsidies.Commands.CreateSubsidyBudget;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ApproveSubsidyBudgetCommandHandler : IRequestHandler<ApproveSubsidyBudgetCommand, SubsidyBudgetResult>
{
    private readonly IWalletDbContext _context;

    public ApproveSubsidyBudgetCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<SubsidyBudgetResult> Handle(ApproveSubsidyBudgetCommand request, CancellationToken cancellationToken)
    {
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var budget = await _context.LockSubsidyBudgetAsync(request.BudgetId, cancellationToken)
            ?? throw new BusinessRuleValidationException("SUBSIDY_BUDGET_NOT_FOUND", "The subsidy budget was not found.", 404);

        if (budget.Status == SubsidyBudgetStatus.Active && budget.ApprovedByAdminId == request.AdminUserId)
        {
            await dbTransaction.CommitAsync(cancellationToken);
            return budget.ToResult();
        }

        if (budget.Status != SubsidyBudgetStatus.PendingApproval)
        {
            throw new BusinessRuleValidationException("SUBSIDY_BUDGET_NOT_PENDING", "Only a pending budget can be approved.", 409);
        }

        if (budget.CreatedByAdminId == request.AdminUserId)
        {
            throw new BusinessRuleValidationException("SUBSIDY_APPROVER_MUST_DIFFER", "A subsidy budget must be approved by a different admin than its creator.", 403);
        }

        var otherActive = await _context.PlatformSubsidyBudgets.AsNoTracking()
            .AnyAsync(item => item.Status == SubsidyBudgetStatus.Active && item.Id != budget.Id, cancellationToken);
        if (otherActive)
        {
            throw new BusinessRuleValidationException("SUBSIDY_ACTIVE_BUDGET_EXISTS", "Another subsidy budget is already active. Close it first.", 409);
        }

        var now = DateTimeOffset.UtcNow;
        budget.Status = SubsidyBudgetStatus.Active;
        budget.ApprovedByAdminId = request.AdminUserId;
        budget.ApprovedAt = now;
        budget.UpdatedAt = now;
        _context.AddAuditLog(
            AuditAction.SubsidyBudgetApprove,
            nameof(Wallet.Domain.Entities.PlatformSubsidyBudget),
            budget.Id.ToString(),
            oldValues: new { Status = nameof(SubsidyBudgetStatus.PendingApproval) },
            newValues: new { Status = nameof(SubsidyBudgetStatus.Active), budget.TotalLimit, budget.DailyLimit, budget.PerOrderCap, budget.EvidenceRef },
            userId: request.AdminUserId.ToString());
        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return budget.ToResult();
    }
}
