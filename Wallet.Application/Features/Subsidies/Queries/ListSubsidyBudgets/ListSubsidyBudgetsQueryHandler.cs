namespace Wallet.Application.Features.Subsidies.Queries.ListSubsidyBudgets;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Models;
using Wallet.Application.Features.Subsidies.Commands.CreateSubsidyBudget;
using Wallet.Application.Interfaces;

internal sealed class ListSubsidyBudgetsQueryHandler : IRequestHandler<ListSubsidyBudgetsQuery, PagedData<SubsidyBudgetResult>>
{
    private readonly IWalletDbContext _context;

    public ListSubsidyBudgetsQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<PagedData<SubsidyBudgetResult>> Handle(ListSubsidyBudgetsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var query = _context.PlatformSubsidyBudgets.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedData<SubsidyBudgetResult>(rows.Select(item => item.ToResult()).ToList(), PaginationMetadata.Create(page, pageSize, total));
    }
}
