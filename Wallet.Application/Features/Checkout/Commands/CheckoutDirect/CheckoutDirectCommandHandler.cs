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
    private readonly IDirectPaymentGateway _paymentGateway;
    private readonly IVnPayService _vnPay;

    public CheckoutDirectCommandHandler(
        IWalletDbContext context,
        IDirectPaymentGateway paymentGateway,
        IVnPayService vnPay)
    {
        _context = context;
        _paymentGateway = paymentGateway;
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

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(buyerId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.UserId);

        var gateway = request.Gateway.Trim();
        var isVnPay = string.Equals(gateway, "VNPAY", StringComparison.OrdinalIgnoreCase);
        if (isVnPay &&
            (!Uri.TryCreate(request.ReturnUrl, UriKind.Absolute, out var returnUri) ||
             returnUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_RETURN_URL",
                "VNPay return URL must be an absolute HTTPS URL.");
        }

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
                    $"Payment for order via {gateway}",
                    StringComparison.Ordinal))
                throw OrderConflict();

            string existingPaymentUrl;
            if (isVnPay)
            {
                var existingOrder = await _context.DirectPaymentOrders
                    .SingleOrDefaultAsync(order => order.OrderId == orderId, cancellationToken);
                var fingerprint = ComputeFingerprint(request, buyerId, orderId, gateway);
                if (existingOrder is null ||
                    existingOrder.Status != TopupStatus.Pending ||
                    existingOrder.Amount != request.Amount ||
                    !string.Equals(existingOrder.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                    throw OrderConflict();

                existingPaymentUrl = CreateVnPayPaymentUrl(
                    existingOrder.VnpTxnRef,
                    orderId,
                    existingOrder.Amount,
                    request.ClientIp,
                    NormalizeOptional(request.BankCode),
                    request.ReturnUrl.Trim());
            }
            else
            {
                existingPaymentUrl = _paymentGateway.CreatePaymentUrl(
                    gateway,
                    orderId,
                    existingTransaction.Amount,
                    wallet.Currency.ToString(),
                    request.BankCode,
                    request.ReturnUrl);
            }

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

        if (isVnPay)
        {
            if (wallet.Currency != CurrencyCode.Vnd)
                throw new BusinessRuleValidationException(
                    "VAL_INVALID_CURRENCY",
                    "VNPay checkout requires a VND wallet.");

            if (request.Amount <= 0 || request.Amount != decimal.Truncate(request.Amount))
                throw new BusinessRuleValidationException(
                    "VAL_INVALID_AMOUNT",
                    "VNPay checkout amount must be a positive whole-VND value.");

            var existingOrder = await _context.DirectPaymentOrders
                .SingleOrDefaultAsync(order => order.OrderId == orderId, cancellationToken);
            if (existingOrder is not null)
                throw OrderConflict();

            var transactionReference = $"DIRECT-{Guid.NewGuid():N}";
            var vnPayPaymentUrl = CreateVnPayPaymentUrl(
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
                RequestFingerprint = ComputeFingerprint(request, buyerId, orderId, gateway)
            };
            var vnPayTransaction = CreateTransaction(wallet, orderId, request.Amount, gateway);
            _context.DirectPaymentOrders.Add(directPayment);
            _context.WalletTransactions.Add(vnPayTransaction);
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
                vnPayTransaction.Id.ToString(),
                request.OrderId,
                vnPayTransaction.Amount,
                vnPayPaymentUrl,
                vnPayTransaction.Status.ToString(),
                vnPayTransaction.CreatedAt);
        }

        string paymentUrl;
        try
        {
            paymentUrl = _paymentGateway.CreatePaymentUrl(
                gateway,
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

        var transaction = CreateTransaction(wallet, orderId, request.Amount, gateway);

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

    private static WalletTransaction CreateTransaction(
        WalletAccount wallet,
        Guid orderId,
        decimal amount,
        string gateway)
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
            Note = $"Payment for order via {gateway}"
        };
    }

    private static string ComputeFingerprint(
        CheckoutDirectCommand request,
        Guid buyerId,
        Guid orderId,
        string gateway)
    {
        var values = string.Join(
            "\n",
            buyerId.ToString("D"),
            orderId.ToString("D"),
            request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            gateway.ToUpperInvariant(),
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