namespace Wallet.Application.Features.Audit.Commands.MarkAuditLogsSynced;

using System;
using System.Collections.Generic;
using MediatR;

public sealed record MarkAuditLogsSyncedCommand(IReadOnlyList<Guid>? Ids) : IRequest<MarkAuditLogsSyncedResult>;

public sealed record MarkAuditLogsSyncedResult(int Updated);
