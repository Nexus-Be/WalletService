namespace Wallet.Application.Features.AdminWallets.Queries.GetWithdrawalHistory;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.Withdrawals;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Exceptions;

internal sealed class GetWithdrawalHistoryQueryHandler(IWalletDbContext context)
    : IRequestHandler<GetWithdrawalHistoryQuery, IReadOnlyList<WithdrawalHistoryEntry>>
{
    public async Task<IReadOnlyList<WithdrawalHistoryEntry>> Handle(
        GetWithdrawalHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var exists = await context.WithdrawalRequests
            .AsNoTracking()
            .AnyAsync(item => item.Id == request.WithdrawalId, cancellationToken);
        if (!exists)
        {
            throw new EntityNotFoundException("WITHDRAWAL_NOT_FOUND", nameof(WithdrawalRequest), request.WithdrawalId);
        }

        var entityId = request.WithdrawalId.ToString();
        var logs = await context.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityName == nameof(WithdrawalRequest) && log.EntityId == entityId)
            .OrderBy(log => log.CreatedAt)
            .ThenBy(log => log.Id)
            .ToListAsync(cancellationToken);

        return logs
            .Select(log => new WithdrawalHistoryEntry(
                log.Id,
                log.Action.ToString(),
                log.UserId,
                SanitizeAuditJson(log.OldValues),
                SanitizeAuditJson(log.NewValues),
                log.CreatedAt))
            .ToArray();
    }

    private static readonly HashSet<string> RedactedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "BankName",
        "BankCode",
        "BankAccountNumber",
        "AccountNumber",
        "BankAccountHolder",
        "AccountHolderName",
        "BankAccountId",
        "VerificationEvidence"
    };

    private static string? SanitizeAuditJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return json;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (RedactedProperties.Contains(property.Name))
                    {
                        continue;
                    }

                    property.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
