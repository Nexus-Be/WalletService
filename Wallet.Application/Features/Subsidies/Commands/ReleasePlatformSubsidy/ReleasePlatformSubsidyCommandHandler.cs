namespace Wallet.Application.Features.Subsidies.Commands.ReleasePlatformSubsidy;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Subsidies.Commands.HoldPlatformSubsidy;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ReleasePlatformSubsidyCommandHandler : IRequestHandler<ReleasePlatformSubsidyCommand, ReleasePlatformSubsidyResponse>
{
    private readonly IWalletDbContext _context;

    public ReleasePlatformSubsidyCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<ReleasePlatformSubsidyResponse> Handle(ReleasePlatformSubsidyCommand request, CancellationToken cancellationToken)
    {
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await _context.LockOrderPaymentAsync(request.SubOrderId, cancellationToken);

        var reservation = await _context.PlatformSubsidyReservations.FirstOrDefaultAsync(item => item.SubOrderId == request.SubOrderId, cancellationToken);
        if (reservation is null)
        {
            await dbTransaction.CommitAsync(cancellationToken);
            return new ReleasePlatformSubsidyResponse(request.SubOrderId.ToString(), "None", 0m, false);
        }

        if (reservation.Status == SubsidyReservationStatus.Released)
        {
            await dbTransaction.CommitAsync(cancellationToken);
            return new ReleasePlatformSubsidyResponse(request.SubOrderId.ToString(), nameof(SubsidyReservationStatus.Released), reservation.Amount, false);
        }

        if (reservation.Status == SubsidyReservationStatus.Used)
        {
            throw new BusinessRuleValidationException("SUBSIDY_ALREADY_USED", "The subsidy was already used by a payout and cannot be released.", 409);
        }

        var budget = await _context.LockSubsidyBudgetAsync(reservation.BudgetId, cancellationToken)
            ?? throw new InvalidOperationException("The subsidy budget of a reservation was not found.");
        if (budget.ReservedAmount < reservation.Amount)
        {
            throw new InvalidOperationException("The subsidy budget reserved total is smaller than one of its reservations.");
        }

        var now = DateTimeOffset.UtcNow;
        budget.ReservedAmount -= reservation.Amount;
        budget.UpdatedAt = now;
        reservation.Status = SubsidyReservationStatus.Released;
        reservation.ReleasedAt = now;
        reservation.ReleaseReason = request.Reason;
        reservation.UpdatedAt = now;
        _context.AddAuditLog(
            AuditAction.SubsidyRelease,
            nameof(PlatformSubsidyReservation),
            reservation.Id.ToString(),
            oldValues: new { Status = nameof(SubsidyReservationStatus.Held) },
            newValues: new { Status = nameof(SubsidyReservationStatus.Released), reservation.Amount, request.Reason, budget.ReservedAmount, budget.UsedAmount });
        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return new ReleasePlatformSubsidyResponse(request.SubOrderId.ToString(), nameof(SubsidyReservationStatus.Released), reservation.Amount, true);
    }
}
