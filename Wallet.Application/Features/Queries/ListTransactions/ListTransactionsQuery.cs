namespace Wallet.Application.Features.Queries.ListTransactions;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.DTOs;

public record ListTransactionsQuery(string UserId, int Page, int PageSize, string? Type) : IRequest<WalletTransactionPageDto>;

public class ListTransactionsQueryHandler : IRequestHandler<ListTransactionsQuery, WalletTransactionPageDto>
{
    private readonly IWalletDbContext _context;

    public ListTransactionsQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletTransactionPageDto> Handle(ListTransactionsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 10 : Math.Min(request.PageSize, 100);

        var query = _context.WalletTransactions
            .AsNoTracking()
            .Where(t => t.UserId == request.UserId);

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            query = query.Where(t => t.Type == request.Type.Trim().ToUpperInvariant());
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new WalletTransactionDto
            {
                Id = t.Id.ToString(),
                WalletId = t.WalletId.ToString(),
                UserId = t.UserId,
                Type = t.Type,
                Amount = t.Amount,
                Currency = t.Currency,
                BalanceAfter = t.BalanceAfter,
                Status = t.Status,
                Note = t.Note,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new WalletTransactionPageDto
        {
            Items = items,
            Total = total
        };
    }
}
