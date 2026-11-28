namespace Wallet.Application.Features.Subsidies.Commands.HoldPlatformSubsidy;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Validation;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class HoldPlatformSubsidyCommandHandler : IRequestHandler<HoldPlatformSubsidyCommand, SubsidyReservationResponse>
{
    private readonly IWalletDbContext _context;

    public HoldPlatformSubsidyCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<SubsidyReservationResponse> Handle(HoldPlatformSubsidyCommand request, CancellationToken cancellationToken)
    {
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        // The sub-order lock is the same one the payout takes, so a hold, a release and a payout of one sub-order never interleave.
        await _context.LockOrderPaymentAsync(request.SubOrderId, cancellationToken);

        var existing = await _context.PlatformSubsidyReservations.FirstOrDefaultAsync(item => item.SubOrderId == request.SubOrderId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Amount != request.Amount || existing.OrderId != request.OrderId)
            {
                throw new BusinessRuleValidationException("SUBSIDY_CONFLICT", "The sub-order already has a subsidy reservation with different details.", 409);
            }

            if (existing.Status == SubsidyReservationStatus.Released)
            {
                throw new BusinessRuleValidationException("SUBSIDY_ALREADY_RELEASED", "The subsidy reservation of this sub-order was already released.", 409);
            }

            await dbTransaction.CommitAsync(cancellationToken);
            return Map(existing, isReplay: true);
        }

        var activeId = await _context.PlatformSubsidyBudgets.AsNoTracking()
            .Where(item => item.Status == SubsidyBudgetStatus.Active)
            .OrderBy(item => item.CreatedAt).Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
        if (activeId is null)
        {
            throw new BusinessRuleValidationException("SUBSIDY_BUDGET_UNAVAILABLE", "No approved platform subsidy budget is available.", 409);
        }

        var budget = await _context.LockSubsidyBudgetAsync(activeId.Value, cancellationToken)
            ?? throw new BusinessRuleValidationException("SUBSIDY_BUDGET_UNAVAILABLE", "No approved platform subsidy budget is available.", 409);
        if (budget.Status != SubsidyBudgetStatus.Active)
        {
            throw new BusinessRuleValidationException("SUBSIDY_BUDGET_UNAVAILABLE", "No approved platform subsidy budget is available.", 409);
        }

        if (request.Amount > budget.PerOrderCap)
        {
            throw new BusinessRuleValidationException("SUBSIDY_PER_ORDER_CAP_EXCEEDED", "The subsidy exceeds the per-order cap.", 409);
        }

        if (budget.ReservedAmount + budget.UsedAmount + request.Amount > budget.TotalLimit)
        {
            throw new BusinessRuleValidationException("SUBSIDY_BUDGET_EXHAUSTED", "The platform subsidy budget is exhausted.", 409);
        }

        var dayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var today = await _context.PlatformSubsidyReservations.AsNoTracking()
            .Where(item => item.BudgetId == budget.Id && item.CreatedAt >= dayStart && item.Status != SubsidyReservationStatus.Released)
            .Select(item => item.Amount).ToListAsync(cancellationToken);
        if (today.Sum() + request.Amount > budget.DailyLimit)
        {
            throw new BusinessRuleValidationException("SUBSIDY_DAILY_LIMIT_EXCEEDED", "The daily platform subsidy limit is reached.", 409);
        }

        var now = DateTimeOffset.UtcNow;
        var reservation = new PlatformSubsidyReservation
        {
            Id = Guid.NewGuid(),
            BudgetId = budget.Id,
            SubOrderId = request.SubOrderId,
            OrderId = request.OrderId,
            Amount = request.Amount,
            Status = SubsidyReservationStatus.Held,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.PlatformSubsidyReservations.Add(reservation);
        budget.ReservedAmount += request.Amount;
        budget.UpdatedAt = now;
        _context.AddAuditLog(
            AuditAction.SubsidyHold,
            nameof(PlatformSubsidyReservation),
            reservation.Id.ToString(),
            newValues: new { reservation.BudgetId, reservation.OrderId, reservation.SubOrderId, reservation.Amount, budget.ReservedAmount, budget.UsedAmount });
        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return Map(reservation, isReplay: false);
    }

    internal static SubsidyReservationResponse Map(PlatformSubsidyReservation item, bool isReplay) => new(
        item.Id.ToString(), item.BudgetId.ToString(), item.OrderId.ToString(), item.SubOrderId.ToString(), item.Amount, item.Status.ToString(), isReplay);
}
