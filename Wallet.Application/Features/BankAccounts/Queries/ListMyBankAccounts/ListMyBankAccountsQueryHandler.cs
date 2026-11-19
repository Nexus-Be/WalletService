namespace Wallet.Application.Features.BankAccounts.Queries.ListMyBankAccounts;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Interfaces;

internal sealed class ListMyBankAccountsQueryHandler(IWalletDbContext context)
    : IRequestHandler<ListMyBankAccountsQuery, IReadOnlyList<BankAccountResponse>>
{
    public async Task<IReadOnlyList<BankAccountResponse>> Handle(
        ListMyBankAccountsQuery request,
        CancellationToken cancellationToken)
    {
        var userId = request.UserId.ToString();
        var items = await context.BankAccounts
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.IsDefault)
            .ThenByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        return items.Select(BankAccountResponse.FromEntity).ToArray();
    }
}
