namespace Wallet.Application.Features.Queries.ListTransactions;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.DTOs;
using Wallet.Domain.Enums;

public record ListTransactionsQuery(
    string UserId,
    int Page,
    int PageSize,
    string? Type,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<WalletTransactionPageDto>;

public class ListTransactionsQueryHandler : IRequestHandler<ListTransactionsQuery, WalletTransactionPageDto>
{
    private readonly IWalletDbContext _context;

    public ListTransactionsQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletTransactionPageDto> Handle(ListTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || userId == Guid.Empty)
            throw new ArgumentException("UserId must be a valid UUID.", nameof(request.UserId));
        if (request.Page <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Page), "Page must be greater than zero.");
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;
        if (pageSize > 100)
            throw new ArgumentOutOfRangeException(nameof(request.PageSize), "PageSize must be between 1 and 100.");
        if (request.Page > int.MaxValue / pageSize)
            throw new ArgumentOutOfRangeException(nameof(request.Page), "Page is too large.");
        if (request.FromDate.HasValue && request.ToDate.HasValue && request.FromDate > request.ToDate)
            throw new ArgumentException("FromDate must not be after ToDate.");

        var query = _context.WalletTransactions
            .AsNoTracking()
            .Where(t => t.UserId == request.UserId);

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!Enum.TryParse<WalletTransactionType>(request.Type.Trim(), true, out var txType))
                throw new ArgumentException("Transaction type is invalid.", nameof(request.Type));

            query = query.Where(t => t.Type == txType);
        }

        if (request.FromDate.HasValue)
            query = query.Where(t => t.CreatedAt >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(t => t.CreatedAt <= request.ToDate.Value);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip((request.Page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new WalletTransactionDto
            {
                Id = t.Id.ToString(),
                WalletId = t.WalletId.ToString(),
                UserId = t.UserId ?? string.Empty,
                Type = t.Type.ToString(),
                Amount = t.Amount,
                Currency = t.Currency ?? "VND",
                BalanceAfter = t.BalanceAfter,
                Status = t.Status.ToString(),
                Note = t.Note ?? string.Empty,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new WalletTransactionPageDto
        {
            Items = items,
            Total = total,
            Page = request.Page,
            PageSize = pageSize
        };
    }
}