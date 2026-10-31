namespace Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;

internal sealed class ProcessVnPayIpnCommandHandler : IRequestHandler<ProcessVnPayIpnCommand, VnPayIpnResponse>
{
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
        if (!_vnPay.ValidateSignature(request.Parameters))
        {
            _logger.LogWarning("Invalid VNPay signature on IPN payload.");
            return new VnPayIpnResponse("97", "Invalid signature");
        }

        request.Parameters.TryGetValue("vnp_TxnRef", out var txnRef);
        request.Parameters.TryGetValue("vnp_ResponseCode", out var responseCode);
        request.Parameters.TryGetValue("vnp_Amount", out var rawAmount);

        if (string.IsNullOrWhiteSpace(txnRef) || string.IsNullOrWhiteSpace(responseCode))
        {
            return new VnPayIpnResponse("99", "Missing parameters");
        }

        if (!decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedAmount))
        {
            return new VnPayIpnResponse("04", "Invalid amount");
        }

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var topup = await _context.LockTopupByReferenceAsync(txnRef, cancellationToken);
        if (topup is null)
        {
            return new VnPayIpnResponse("01", "Order not found");
        }

        if (topup.Status != TopupStatus.Pending)
        {
            return new VnPayIpnResponse("02", "Order already processed");
        }

        if (topup.Amount * 100 != parsedAmount)
        {
            return new VnPayIpnResponse("04", "Invalid amount");
        }

        if (responseCode == "00")
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

        return new VnPayIpnResponse("00", "Success");
    }
}
