namespace Wallet.Application.Features.Ledger.Queries.GetLedgerStatus;

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

public sealed record GetLedgerStatusQuery : IRequest<LedgerStatusResult>;

public sealed record LedgerWalletStatus(
    Guid WalletId, int LedgerEpoch, int HistoryMissingCount, string HistoryStatus, int LivePendingCount, int LiveFailedCount,
    long? OldestPendingAgeSeconds, string I10Result, string? I10Reason);

public sealed record LedgerStatusResult(
    bool Enabled, int ShadowEpoch, int Version, DateTimeOffset UpdatedAt, int MatchedCount, int MismatchedCount, int UncheckedCount,
    IReadOnlyList<LedgerWalletStatus> Wallets);
