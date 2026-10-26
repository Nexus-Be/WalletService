namespace Wallet.Application.Features.Commands.Topups;

using System.Globalization;
using System.Net;
using System.Text.Json;
using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Events;
using Wallet.Domain.Exceptions;

public sealed record InitiateVnPayTopupCommand(
    string UserId,
    decimal Amount,
    string IpAddress) : IRequest<VnPayTopupDto>;

public sealed record VnPayTopupDto(
    string TransactionReference,
    string PaymentUrl,
    string TopupId,
    string WalletId,
    string UserId,
    decimal Amount,
    decimal Balance);

public sealed class InitiateVnPayTopupCommandValidator : AbstractValidator<InitiateVnPayTopupCommand>
{
    public InitiateVnPayTopupCommandValidator()
    {
        RuleFor(x => x.UserId).Must(x => Guid.TryParse(x, out var userId) && userId != Guid.Empty)
            .WithMessage("UserId must be a valid UUID.");
        RuleFor(x => x.Amount).InclusiveBetween(10_000m, 100_000_000m);
        RuleFor(x => x.Amount).Must(x => decimal.Round(x, 2) == x)
            .WithMessage("Amount must have no more than two decimal places.");
        RuleFor(x => x.IpAddress).Must(x => IPAddress.TryParse(x, out _))
            .WithMessage("IpAddress must be a valid IP address.");
    }
}

public sealed class InitiateVnPayTopupCommandHandler : IRequestHandler<InitiateVnPayTopupCommand, VnPayTopupDto>
{
    private readonly IWalletDbContext _context;
    private readonly IVnPayService _vnPay;
    private readonly IPublishEndpoint _publishEndpoint;

    public InitiateVnPayTopupCommandHandler(
        IWalletDbContext context,
        IVnPayService vnPay,
        IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _vnPay = vnPay;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<VnPayTopupDto> Handle(InitiateVnPayTopupCommand request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(request.UserId);
        var wallet = await _context.Wallets.SingleOrDefaultAsync(w => w.UserId == userId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.UserId);

        var reference = $"TOPUP-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var paymentUrl = _vnPay.CreatePaymentUrl(reference, request.Amount, request.IpAddress);

        var topup = new WalletTopup
        {
            WalletId = wallet.Id,
            VnpTxnRef = reference,
            Amount = request.Amount,
            Status = TopupStatus.Pending
        };
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        _context.WalletTopups.Add(topup);
        _context.AuditLogs.Add(new AuditLog
        {
            ServiceName = "Wallet.Api",
            UserId = userId.ToString(),
            Action = AuditAction.TopupRequest,
            EntityName = nameof(WalletTopup),
            EntityId = topup.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new { topup.VnpTxnRef, topup.Amount, topup.Status })
        });

        await _context.SaveChangesAsync(cancellationToken);
        await _publishEndpoint.Publish<IWalletTopupInitiatedEvent>(new
        {
            WalletId = wallet.Id.ToString(),
            TransactionReference = reference,
            Amount = request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return new VnPayTopupDto(
            reference,
            paymentUrl,
            topup.Id.ToString(),
            wallet.Id.ToString(),
            wallet.UserId.ToString(),
            request.Amount,
            wallet.Balance);
    }
}

public sealed record ProcessVnPayIpnCommand(
    IReadOnlyDictionary<string, string> Parameters,
    string? ClientIp) : IRequest<VnPayIpnResult>;

public sealed class ProcessVnPayIpnCommandHandler : IRequestHandler<ProcessVnPayIpnCommand, VnPayIpnResult>
{
    private readonly IWalletDbContext _context;
    private readonly IVnPayService _vnPay;
    private readonly IPublishEndpoint _publishEndpoint;

    public ProcessVnPayIpnCommandHandler(
        IWalletDbContext context,
        IVnPayService vnPay,
        IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _vnPay = vnPay;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<VnPayIpnResult> Handle(ProcessVnPayIpnCommand request, CancellationToken cancellationToken)
    {
        if (!_vnPay.ValidateSignature(request.Parameters))
            return new VnPayIpnResult("97", "Invalid Signature");

        var transactionReference = GetParameter(request.Parameters, "vnp_TxnRef");
        var responseCode = GetParameter(request.Parameters, "vnp_ResponseCode");
        var transactionNo = GetParameter(request.Parameters, "vnp_TransactionNo");
        if (string.IsNullOrWhiteSpace(transactionReference) || string.IsNullOrWhiteSpace(responseCode))
            return new VnPayIpnResult("97", "Invalid Signature");

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var topup = await _context.LockTopupByReferenceAsync(transactionReference, cancellationToken);

        if (topup is null)
            return new VnPayIpnResult("01", "Order not found");

        var logAlreadyExists = await _context.VnpayIpnLogs
            .AnyAsync(
                x => x.VnpTxnRef == transactionReference &&
                     x.ProcessingStatus == IpnProcessingStatus.Processed,
                cancellationToken);
        if (logAlreadyExists || topup.Status != TopupStatus.Pending)
            return new VnPayIpnResult("02", "Order already confirmed");

        var amountValue = GetParameter(request.Parameters, "vnp_Amount");
        if (!decimal.TryParse(amountValue, NumberStyles.None, CultureInfo.InvariantCulture,
                out var amountInMinorUnits) ||
            amountInMinorUnits / 100m != topup.Amount)
        {
            AddIpnLog(topup, request, responseCode, transactionNo, IpnProcessingStatus.InvalidAmount);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new VnPayIpnResult("04", "Invalid amount");
        }

        if (!string.Equals(responseCode, "00", StringComparison.Ordinal))
        {
            topup.Status = TopupStatus.Failed;
            topup.VnpTransactionNo = transactionNo;
            topup.UpdatedAt = DateTimeOffset.UtcNow;
            AddIpnLog(topup, request, responseCode, transactionNo, IpnProcessingStatus.Processed);
            _context.AuditLogs.Add(new AuditLog
            {
                ServiceName = "Wallet.Api",
                Action = AuditAction.TopupFail,
                EntityName = nameof(WalletTopup),
                EntityId = topup.Id.ToString(),
                NewValues = JsonSerializer.Serialize(new { topup.VnpTxnRef, topup.Status, responseCode })
            });
            await _context.SaveChangesAsync(cancellationToken);
            await _publishEndpoint.Publish<IWalletTopupFailedEvent>(new
            {
                WalletId = topup.WalletId.ToString(),
                TransactionReference = topup.VnpTxnRef,
                Reason = $"VNPay response code: {responseCode}",
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new VnPayIpnResult("00", "Confirm Success");
        }

        var wallet = await _context.LockWalletByIdAsync(topup.WalletId, cancellationToken)
                     ?? throw new WalletNotFoundException(topup.WalletId.ToString());

        var balanceBefore = wallet.Balance;
        wallet.Deposit(topup.Amount);
        topup.Status = TopupStatus.Success;
        topup.VnpTransactionNo = transactionNo;
        topup.UpdatedAt = DateTimeOffset.UtcNow;

        var walletTransaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.TopupVnpay,
            Amount = topup.Amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            ReferenceId = topup.Id,
            Status = TransactionStatus.Success,
            Note = $"Nạp tiền VNPay {topup.VnpTxnRef}"
        };
        _context.WalletTransactions.Add(walletTransaction);
        AddIpnLog(topup, request, responseCode, transactionNo, IpnProcessingStatus.Processed);
        _context.AuditLogs.Add(new AuditLog
        {
            ServiceName = "Wallet.Api",
            Action = AuditAction.TopupSuccess,
            EntityName = nameof(WalletTopup),
            EntityId = topup.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new { topup.VnpTxnRef, topup.Amount, topup.Status })
        });

        await _context.SaveChangesAsync(cancellationToken);
        await _publishEndpoint.Publish<IWalletTopupSucceededEvent>(new
        {
            WalletId = wallet.Id.ToString(),
            TransactionId = walletTransaction.Id.ToString(),
            Amount = topup.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new VnPayIpnResult("00", "Confirm Success");
    }

    private void AddIpnLog(
        WalletTopup topup,
        ProcessVnPayIpnCommand request,
        string responseCode,
        string? transactionNo,
        IpnProcessingStatus status)
    {
        _context.VnpayIpnLogs.Add(new VnpayIpnLog
        {
            VnpTxnRef = topup.VnpTxnRef,
            VnpResponseCode = responseCode,
            VnpTransactionNo = transactionNo,
            ClientIp = request.ClientIp,
            RawPayload = JsonSerializer.Serialize(request.Parameters),
            ProcessingStatus = status
        });
    }

    private static string? GetParameter(IReadOnlyDictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out var value) ? value : null;
    }
}