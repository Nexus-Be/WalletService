namespace Wallet.Application.Features.AdminWallets.Queries.GetWithdrawalHistory;

using System;
using System.Collections.Generic;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Withdrawals;

public sealed record GetWithdrawalHistoryQuery(
    [NonEmptyGuid] Guid WithdrawalId) : IRequest<IReadOnlyList<WithdrawalHistoryEntry>>;
