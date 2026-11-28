namespace Wallet.Application.Features.Subsidies.Commands.CloseSubsidyBudget;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.Subsidies.Commands.CreateSubsidyBudget;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class CloseSubsidyBudgetCommandHandler : IRequestHandler<CloseSubsidyBudgetCommand, SubsidyBudgetResult>
{
    private readonly IWalletDbContext _context;

    public CloseSubsidyBudgetCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<SubsidyBudgetResult> Handle(CloseSubsidyBudgetCommand request, CancellationToken cancellationToken)
    {
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var budget = await _context.LockSubsidyBudgetAsync(request.BudgetId, cancellationToken)
            ?? throw new BusinessRuleValidationException("SUBSIDY_BUDGET_NOT_FOUND", "The subsidy budget was not found.", 404);

        if (budget.Status == SubsidyBudgetStatus.Closed)
        {
            await dbTransaction.CommitAsync(cancellationToken);
            return budget.ToResult();
        }

        if (budget.Status != SubsidyBudgetStatus.Active)
        {
            throw new BusinessRuleValidationException("SUBSIDY_BUDGET_NOT_ACTIVE", "Only an active budget can be closed.", 409);
        }

        budget.Status = SubsidyBudgetStatus.Closed;
        budget.UpdatedAt = DateTimeOffset.UtcNow;
        _context.AddAuditLog(
            AuditAction.SubsidyBudgetApprove,
            nameof(Wallet.Domain.Entities.PlatformSubsidyBudget),
            budget.Id.ToString(),
            oldValues: new { Status = nameof(SubsidyBudgetStatus.Active) },
            newValues: new { Status = nameof(SubsidyBudgetStatus.Closed), budget.ReservedAmount, budget.UsedAmount },
            userId: request.AdminUserId.ToString());
        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return budget.ToResult();
    }
}
