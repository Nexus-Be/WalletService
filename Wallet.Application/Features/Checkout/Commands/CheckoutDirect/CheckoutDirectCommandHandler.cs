namespace Wallet.Application.Features.Checkout.Commands.CheckoutDirect;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class CheckoutDirectCommandHandler : IRequestHandler<CheckoutDirectCommand, DirectPaymentResponse>
{
    private readonly IWalletDbContext _context;
    private readonly IDirectPaymentGateway _paymentGateway;

    public CheckoutDirectCommandHandler(IWalletDbContext context, IDirectPaymentGateway paymentGateway)
    {
        _context = context;
        _paymentGateway = paymentGateway;
    }

    public async Task<DirectPaymentResponse> Handle(CheckoutDirectCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var buyerId) || buyerId == Guid.Empty)
        {
            throw new BusinessRuleValidationException("VAL_INVALID_USER_ID", "Missing or empty x-user-id header.");
        }

        if (!Guid.TryParse(request.OrderId, out var orderId) || orderId == Guid.Empty)
        {
            throw new BusinessRuleValidationException("VAL_INVALID_ORDER_ID", "Order ID must be a valid UUID.");
        }

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(buyerId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.UserId);

        var existingTransaction = await _context.WalletTransactions
            .SingleOrDefaultAsync(
                t => t.ReferenceId == orderId && t.Type == WalletTransactionType.DirectGatewayPayment,
                cancellationToken);

        if (existingTransaction is not null)
        {
            if (existingTransaction.WalletId != wallet.Id ||
                existingTransaction.Status != TransactionStatus.Pending ||
                existingTransaction.Amount != request.Amount ||
                !string.Equals(
                    existingTransaction.Note,
                    $"Payment for order via {request.Gateway.Trim()}",
                    StringComparison.Ordinal))
                throw new BusinessRuleValidationException("Order already has a direct payment transaction.");

            var existingPaymentUrl = _paymentGateway.CreatePaymentUrl(
                request.Gateway.Trim(),
                orderId,
                existingTransaction.Amount,
                wallet.Currency.ToString(),
                request.BankCode,
                request.ReturnUrl);

            await dbTransaction.CommitAsync(cancellationToken);
            return new DirectPaymentResponse(
                existingTransaction.Id.ToString(),
                request.OrderId,
                existingTransaction.Amount,
                existingPaymentUrl,
                existingTransaction.Status.ToString(),
                existingTransaction.CreatedAt
            );
        }

        string paymentUrl;
        try
        {
            paymentUrl = _paymentGateway.CreatePaymentUrl(
                request.Gateway.Trim(),
                orderId,
                request.Amount,
                wallet.Currency.ToString(),
                request.BankCode,
                request.ReturnUrl);
        }
        catch (InvalidOperationException)
        {
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            throw new BusinessRuleValidationException("Payment gateway is currently unavailable.");
        }

        var transaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment,
            Amount = request.Amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = wallet.Balance,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Pending,
            ReferenceId = orderId,
            Note = $"Payment for order via {request.Gateway.Trim()}"
        };

        _context.WalletTransactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new DirectPaymentResponse(
            transaction.Id.ToString(),
            request.OrderId,
            request.Amount,
            paymentUrl,
            transaction.Status.ToString(),
            transaction.CreatedAt
        );
    }
}
