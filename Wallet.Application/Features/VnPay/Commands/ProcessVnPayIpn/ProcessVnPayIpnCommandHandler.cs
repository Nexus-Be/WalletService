namespace Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;

using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;

internal sealed class ProcessVnPayIpnCommandHandler : IRequestHandler<ProcessVnPayIpnCommand, VnPayIpnResponse>
{
    private const int MaxReferenceLength = 100;
    private const int MaxPayloadParameters = 50;
    private const int MaxPayloadValueLength = 500;
    private readonly IWalletDbContext _context;
    private readonly IVnPayService _vnPay;
    private readonly ILogger<ProcessVnPayIpnCommandHandler> _logger;

    public ProcessVnPayIpnCommandHandler(
        IWalletDbContext context,
        IVnPayService vnPay,
        ILogger<ProcessVnPayIpnCommandHandler> logger)
    {
        _context = context;
        _vnPay = vnPay;
        _logger = logger;
    }

    public async Task<VnPayIpnResponse> Handle(ProcessVnPayIpnCommand request, CancellationToken cancellationToken)
    {
        var response = await ProcessAsync(request, cancellationToken);
        await TryRecordCallbackAsync(request, response, cancellationToken);
        return response;
    }

    private async Task<VnPayIpnResponse> ProcessAsync(ProcessVnPayIpnCommand request, CancellationToken cancellationToken)
    {
        if (!_vnPay.ValidateSignature(request.Parameters))
        {
            _logger.LogWarning("Invalid VNPay signature on IPN payload.");
            return new VnPayIpnResponse("97", "Invalid signature");
        }

        var txnRef = GetParameter(request.Parameters, "vnp_TxnRef");
        var responseCode = GetParameter(request.Parameters, "vnp_ResponseCode");
        var rawAmount = GetParameter(request.Parameters, "vnp_Amount");

        if (string.IsNullOrWhiteSpace(txnRef) || string.IsNullOrWhiteSpace(responseCode))
        {
            return new VnPayIpnResponse("99", "Missing parameters");
        }

        // VNPay marks a payment as paid only when both the response code and the transaction status are "00".
        var transactionStatus = GetParameter(request.Parameters, "vnp_TransactionStatus");
        var paymentSucceeded = responseCode == "00" &&
                               (string.IsNullOrWhiteSpace(transactionStatus) || transactionStatus == "00");

        // VNPay callbacks omit vnp_CurrCode; when supplied it must be VND.
        var currency = GetParameter(request.Parameters, "vnp_CurrCode");
        if ((currency is not null && !string.Equals(currency, "VND", StringComparison.OrdinalIgnoreCase)) ||
            !long.TryParse(rawAmount, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedAmount))
        {
            return new VnPayIpnResponse("04", "Invalid amount or currency");
        }

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var topup = await _context.LockTopupByReferenceAsync(txnRef, cancellationToken);
        if (topup is null)
        {
            return await ProcessDirectPaymentIpnAsync(
                txnRef,
                paymentSucceeded,
                parsedAmount,
                request,
                dbTransaction,
                cancellationToken);
        }

        if (topup.Status != TopupStatus.Pending)
        {
            return new VnPayIpnResponse("02", "Order already confirmed");
        }

        if (topup.Amount * 100m != parsedAmount)
        {
            return new VnPayIpnResponse("04", "Invalid amount or currency");
        }

        if (paymentSucceeded)
        {
            var wallet = await _context.LockWalletByIdAsync(topup.WalletId, cancellationToken);
            if (wallet is null)
            {
                _logger.LogWarning(
                    "VNPay IPN references wallet '{WalletId}' that no longer exists for topup '{TxnRef}'.",
                    topup.WalletId,
                    txnRef);
                return new VnPayIpnResponse("01", "Wallet not found for topup");
            }

            topup.Status = TopupStatus.Success;
            wallet.Deposit(topup.Amount);
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = wallet.Id,
                UserId = wallet.UserId.ToString(),
                Type = WalletTransactionType.TopupVnpay,
                Amount = topup.Amount,
                Currency = wallet.Currency.ToString(),
                BalanceBefore = wallet.Balance - topup.Amount,
                BalanceAfter = wallet.Balance,
                Status = TransactionStatus.Success,
                Note = $"VNPay topup {txnRef}",
                ReferenceId = topup.Id
            });
            _context.AddAuditLog(
                action: AuditAction.TopupSuccess,
                entityName: nameof(WalletTopup),
                entityId: topup.Id.ToString(),
                newValues: new { topup.VnpTxnRef, topup.Amount, topup.Status },
                userId: wallet.UserId.ToString());
        }
        else
        {
            topup.Status = TopupStatus.Failed;
            _context.AddAuditLog(
                action: AuditAction.TopupFail,
                entityName: nameof(WalletTopup),
                entityId: topup.Id.ToString(),
                newValues: new { topup.VnpTxnRef, topup.Amount, topup.Status, ResponseCode = responseCode });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new VnPayIpnResponse("00", "Confirm Success");
    }

    private async Task<VnPayIpnResponse> ProcessDirectPaymentIpnAsync(
        string transactionReference,
        bool paymentSucceeded,
        long parsedAmount,
        ProcessVnPayIpnCommand request,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction dbTransaction,
        CancellationToken cancellationToken)
    {
        var directPayment = await _context.LockDirectPaymentOrderByReferenceAsync(
            transactionReference,
            cancellationToken);
        if (directPayment is null)
            return new VnPayIpnResponse("01", "Order not found");

        if (directPayment.Status != TopupStatus.Pending)
            return new VnPayIpnResponse("02", "Order already confirmed");

        if (directPayment.Amount * 100m != parsedAmount)
            return new VnPayIpnResponse("04", "Invalid amount or currency");

        var transaction = await _context.WalletTransactions.SingleOrDefaultAsync(
            item => item.ReferenceId == directPayment.OrderId &&
                    item.Type == WalletTransactionType.DirectGatewayPayment,
            cancellationToken);
        if (transaction is null || transaction.Status != TransactionStatus.Pending)
            return new VnPayIpnResponse("01", "Order not found");

        var providerTransactionNo = GetParameter(request.Parameters, "vnp_TransactionNo");
        var oldStatus = directPayment.Status;
        directPayment.Status = paymentSucceeded ? TopupStatus.Success : TopupStatus.Failed;
        directPayment.VnpTransactionNo = providerTransactionNo;
        directPayment.RawResponse = JsonSerializer.Serialize(request.Parameters);
        transaction.Status = paymentSucceeded ? TransactionStatus.Success : TransactionStatus.Failed;
        transaction.UpdatedAt = DateTimeOffset.UtcNow;

        _context.AddAuditLog(
            action: paymentSucceeded ? AuditAction.DirectPaymentSuccess : AuditAction.DirectPaymentFail,
            entityName: nameof(DirectPaymentOrder),
            entityId: directPayment.Id.ToString(),
            oldValues: new { Status = oldStatus },
            newValues: new
            {
                directPayment.OrderId,
                directPayment.VnpTxnRef,
                directPayment.VnpTransactionNo,
                DirectPaymentStatus = directPayment.Status,
                TransactionStatus = transaction.Status
            },
            userId: transaction.UserId);

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return new VnPayIpnResponse("00", "Confirm Success");
    }

    // Recorded outside the settlement transaction so rejected callbacks (bad signature, unknown order, ...) are kept too.
    private async Task TryRecordCallbackAsync(
        ProcessVnPayIpnCommand request,
        VnPayIpnResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            _context.VnpayIpnLogs.Add(new VnpayIpnLog
            {
                Id = Guid.NewGuid(),
                VnpTxnRef = Truncate(GetParameter(request.Parameters, "vnp_TxnRef"), MaxReferenceLength) ?? string.Empty,
                VnpResponseCode = Truncate(GetParameter(request.Parameters, "vnp_ResponseCode"), 50),
                VnpTransactionNo = Truncate(GetParameter(request.Parameters, "vnp_TransactionNo"), 100),
                ClientIp = Truncate(request.ClientIp, 50),
                RawPayload = SerializePayload(request.Parameters),
                ProcessingStatus = response.ResponseCode switch
                {
                    "00" => IpnProcessingStatus.Processed,
                    "02" => IpnProcessingStatus.Duplicate,
                    "97" => IpnProcessingStatus.InvalidSignature,
                    "01" => IpnProcessingStatus.OrderNotFound,
                    "04" => IpnProcessingStatus.InvalidAmount,
                    _ => IpnProcessingStatus.SystemError
                }
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to record the VNPay IPN callback; the acknowledgement is unaffected.");
        }
    }

    private static string SerializePayload(IReadOnlyDictionary<string, string> parameters) =>
        JsonSerializer.Serialize(parameters
            .Take(MaxPayloadParameters)
            .ToDictionary(parameter => parameter.Key, parameter => Truncate(parameter.Value, MaxPayloadValueLength)));

    private static string? Truncate(string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? value[..maxLength] : value;

    private static string? GetParameter(IReadOnlyDictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out var value)
            ? value
            : parameters.FirstOrDefault(parameter =>
                parameter.Key.Equals(key, System.StringComparison.OrdinalIgnoreCase)).Value;
    }
}