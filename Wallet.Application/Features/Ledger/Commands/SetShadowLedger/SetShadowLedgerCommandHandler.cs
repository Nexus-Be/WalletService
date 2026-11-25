namespace Wallet.Application.Features.Ledger.Commands.SetShadowLedger;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Ledger;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

using Wallet.Application.Common.Extensions;

internal sealed class SetShadowLedgerCommandHandler : IRequestHandler<SetShadowLedgerCommand, SetShadowLedgerResult>
{
    private readonly IWalletDbContext _context;

    public SetShadowLedgerCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<SetShadowLedgerResult> Handle(SetShadowLedgerCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // The exclusive lock waits for money transactions that already hold the shared lock, and holds new ones back until this commits.
        var settings = _context.Database.IsRelational()
            ? await _context.LedgerSettings.FromSqlRaw("SELECT * FROM \"LedgerSettings\" WHERE \"Id\" = 1 FOR UPDATE").FirstOrDefaultAsync(cancellationToken)
            : await _context.LedgerSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new LedgerSettings();
            _context.LedgerSettings.Add(settings);
        }

        if (request.ExpectedVersion != settings.Version)
            throw new DomainException("LEDGER_SETTINGS_VERSION_CONFLICT", "The shadow ledger settings changed since they were read.", 409);

        // Switching to the state it is already in changes nothing, so repeating the command is safe.
        if (settings.Enabled == request.Enabled)
            return new SetShadowLedgerResult(settings.Enabled, settings.ShadowEpoch, settings.Version, false);

        var before = new { settings.Enabled, settings.ShadowEpoch, settings.Version };
        settings.Enabled = request.Enabled;
        if (request.Enabled) settings.ShadowEpoch++;
        settings.Version++;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = request.AdminUserId;

        _context.AddAuditLog(
            AuditAction.LedgerShadowSwitch,
            nameof(LedgerSettings), "1",
            oldValues: before,
            newValues: new { settings.Enabled, settings.ShadowEpoch, settings.Version },
            userId: request.AdminUserId.ToString());
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SetShadowLedgerResult(settings.Enabled, settings.ShadowEpoch, settings.Version, true);
    }
}
