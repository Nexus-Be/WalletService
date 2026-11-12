namespace Wallet.Application.Features.Checkout.Queries.GetDirectPaymentStatus;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class GetDirectPaymentStatusQueryHandler
    : IRequestHandler<GetDirectPaymentStatusQuery, DirectPaymentStatusResponse>
{
    private readonly IWalletDbContext _context;

    public GetDirectPaymentStatusQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<DirectPaymentStatusResponse> Handle(
        GetDirectPaymentStatusQuery request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.OrderId, out var orderId) || orderId == Guid.Empty)
        {
            throw new BusinessRuleValidationException("VAL_INVALID_ORDER_ID", "Order ID must be a valid UUID.");
        }

        var userId = string.Empty;
        if (!request.AllowAnyUser)
        {
            if (!Guid.TryParse(request.UserId, out var userGuid) || userGuid == Guid.Empty)
            {
                throw new BusinessRuleValidationException("VAL_INVALID_USER_ID", "Missing or empty x-user-id header.");
            }

            userId = userGuid.ToString();
        }

        var transaction = await _context.WalletTransactions
            .AsNoTracking()
            .Where(t => t.ReferenceId == orderId && t.Type == WalletTransactionType.DirectGatewayPayment)
            .Where(t => request.AllowAnyUser || t.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("PAYMENT_NOT_FOUND", "DirectPayment", orderId);

        var order = await _context.DirectPaymentOrders
            .AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new { o.Id, o.VnpTxnRef, o.VnpTransactionNo })
            .FirstOrDefaultAsync(cancellationToken);

        var gatewayTransactionNo = order?.VnpTransactionNo;
        if (order is not null && order.VnpTxnRef is null)
        {
            gatewayTransactionNo = await _context.StripePaymentAttempts
                .AsNoTracking()
                .Where(a => a.DirectPaymentOrderId == order.Id)
                .OrderByDescending(a => a.Status == StripePaymentAttemptStatus.Successful)
                .ThenByDescending(a => a.CreatedAt)
                .ThenByDescending(a => a.Id)
                .Select(a => a.PaymentIntentId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new DirectPaymentStatusResponse(
            transaction.Id.ToString(),
            orderId.ToString(),
            transaction.Amount,
            transaction.Currency ?? string.Empty,
            transaction.Status.ToString(),
            gatewayTransactionNo,
            transaction.CreatedAt,
            transaction.UpdatedAt);
    }
}
