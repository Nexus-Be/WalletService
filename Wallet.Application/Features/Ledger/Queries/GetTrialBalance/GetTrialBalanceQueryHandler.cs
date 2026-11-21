namespace Wallet.Application.Features.Ledger.Queries.GetTrialBalance;

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

internal sealed class GetTrialBalanceQueryHandler : IRequestHandler<GetTrialBalanceQuery, TrialBalanceResult>
{
    private readonly IWalletDbContext _context;

    public GetTrialBalanceQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<TrialBalanceResult> Handle(GetTrialBalanceQuery request, CancellationToken cancellationToken)
    {
        var accounts = await _context.JournalLines.AsNoTracking().GroupBy(line => line.AccountCode)
            .Select(group => new TrialBalanceAccount(group.Key, group.Sum(line => line.Debit), group.Sum(line => line.Credit)))
            .OrderBy(account => account.Account).ToListAsync(cancellationToken);
        var debit = accounts.Sum(account => account.TotalDebit);
        var credit = accounts.Sum(account => account.TotalCredit);
        return new TrialBalanceResult(accounts, debit, credit, debit == credit);
    }
}
