namespace Wallet.Application.Features.Checkout.Commands.CheckoutDirect;

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class CheckoutDirectCommandHandler : IRequestHandler<CheckoutDirectCommand, DirectPaymentResponse>
{
    private readonly IWalletDbContext _context;
    private readonly IVnPayService _vnPay;

    public CheckoutDirectCommandHandler(IWalletDbContext context, IVnPayService vnPay)
    {
        _context = context;
        _vnPay = vnPay;
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

        if (!Uri.TryCreate(request.ReturnUrl, UriKind.Absolute, out var returnUri) ||
            returnUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_RETURN_URL",
                "VNPay return URL must be an absolute HTTPS URL.");
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
                (!string.Equals(existingTransaction.Note, BuildNote(orderId), StringComparison.Ordinal) &&
                 !string.Equals(existingTransaction.Note, "Payment for order via VNPAY", StringComparison.Ordinal)))
                throw OrderConflict();

            var existingOrder = await _context.DirectPaymentOrders
                .SingleOrDefaultAsync(order => order.OrderId == orderId, cancellationToken);
            var fingerprint = ComputeFingerprint(request, buyerId, orderId);
            if (existingOrder is null ||
                existingOrder.Status != TopupStatus.Pending ||
                existingOrder.Amount != request.Amount ||
                !string.Equals(existingOrder.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                throw OrderConflict();

            var existingPaymentUrl = CreateVnPayPaymentUrl(
                existingOrder.VnpTxnRef,
                orderId,
                existingOrder.Amount,
                request.ClientIp,
                NormalizeOptional(request.BankCode),
                request.ReturnUrl.Trim());

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

        if (wallet.Currency != CurrencyCode.Vnd)
            throw new BusinessRuleValidationException(
                "VAL_INVALID_CURRENCY",
                "VNPay checkout requires a VND wallet.");

        if (request.Amount <= 0 || request.Amount != decimal.Truncate(request.Amount))
            throw new BusinessRuleValidationException(
                "VAL_INVALID_AMOUNT",
                "VNPay checkout amount must be a positive whole-VND value.");

        var existingPaymentOrder = await _context.DirectPaymentOrders
            .SingleOrDefaultAsync(order => order.OrderId == orderId, cancellationToken);
        if (existingPaymentOrder is not null)
            throw OrderConflict();

        var transactionReference = $"DIRECT-{Guid.NewGuid():N}";
        var paymentUrl = CreateVnPayPaymentUrl(
            transactionReference,
            orderId,
            request.Amount,
            request.ClientIp,
            NormalizeOptional(request.BankCode),
            request.ReturnUrl.Trim());

        var directPayment = new DirectPaymentOrder
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            VnpTxnRef = transactionReference,
            Amount = request.Amount,
            Status = TopupStatus.Pending,
            RequestFingerprint = ComputeFingerprint(request, buyerId, orderId)
        };
        var transaction = CreateTransaction(wallet, orderId, request.Amount);
        _context.DirectPaymentOrders.Add(directPayment);
        _context.WalletTransactions.Add(transaction);
        _context.AddAuditLog(
            action: AuditAction.DirectPaymentRequest,
            entityName: nameof(DirectPaymentOrder),
            entityId: directPayment.Id.ToString(),
            newValues: new
            {
                directPayment.OrderId,
                directPayment.VnpTxnRef,
                directPayment.Amount,
                directPayment.Status
            },
            userId: wallet.UserId.ToString(),
            ipAddress: request.ClientIp);

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new DirectPaymentResponse(
            transaction.Id.ToString(),
            request.OrderId,
            transaction.Amount,
            paymentUrl,
            transaction.Status.ToString(),
            transaction.CreatedAt);
    }

    private static WalletTransaction CreateTransaction(
        WalletAccount wallet,
        Guid orderId,
        decimal amount)
    {
        return new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment,
            Amount = amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = wallet.Balance,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Pending,
            ReferenceId = orderId,
            Note = BuildNote(orderId)
        };
    }

    private static string BuildNote(Guid orderId) => $"Payment for order {orderId:D} via VNPAY";

    private static string ComputeFingerprint(
        CheckoutDirectCommand request,
        Guid buyerId,
        Guid orderId)
    {
        var values = string.Join(
            "\n",
            buyerId.ToString("D"),
            orderId.ToString("D"),
            request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            "VNPAY",
            NormalizeOptional(request.BankCode) ?? string.Empty,
            request.ReturnUrl.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(values)));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string CreateVnPayPaymentUrl(
        string transactionReference,
        Guid orderId,
        decimal amount,
        string clientIp,
        string? bankCode,
        string returnUrl)
    {
        try
        {
            return _vnPay.CreateDirectPaymentUrl(
                transactionReference,
                orderId,
                amount,
                clientIp,
                bankCode,
                returnUrl);
        }
        catch (InvalidOperationException)
        {
            throw new BusinessRuleValidationException(
                "PAYMENT_GATEWAY_UNAVAILABLE",
                "VNPay is currently unavailable.",
                502);
        }
    }

    private static BusinessRuleValidationException OrderConflict() =>
        new("PAYMENT_ORDER_CONFLICT", "The order already has a different or completed payment.", 409);
}