namespace Wallet.Application.Features.Audit.Queries.GetUnsyncedAuditLogs;

using System;
using System.Collections.Generic;
using MediatR;

public sealed record GetUnsyncedAuditLogsQuery(int Limit = 200) : IRequest<UnsyncedAuditLogsResult>;

public sealed record AuditSyncItem(
    Guid Id,
    string ServiceName,
    string? TraceId,
    string? UserId,
    string? UserName,
    string Action,
    string EntityName,
    string EntityId,
    string? OldValues,
    string? NewValues,
    string? ChangedColumns,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt);

public sealed record UnsyncedAuditLogsResult(IReadOnlyList<AuditSyncItem> Items, int UnsyncedCount);
