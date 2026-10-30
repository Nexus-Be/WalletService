namespace Wallet.Application.Services.Wallet;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using global::Wallet.Application.Common.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

public sealed class WalletService : IWalletService
{
    private readonly IWalletDbContext _context;
    private readonly IVnPayService _vnPay;
    private readonly IDirectPaymentGateway _paymentGateway;
    private readonly ILogger<WalletService> _logger;

    public WalletService(
        IWalletDbContext context,
        IVnPayService vnPay,
        IDirectPaymentGateway paymentGateway,
        ILogger<WalletService> logger)
    {
        _context = context;
        _vnPay = vnPay;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<WalletResponse> GetWalletByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(userId, out var userGuid) || userGuid == Guid.Empty)
            throw new ArgumentException("User ID must be a valid UUID.", nameof(userId));

        var wallet = await _context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userGuid, cancellationToken);

        if (wallet == null)
        {
            // Lazy auto-creation for active user
            wallet = new WalletAccount
            {
                UserId = userGuid,
                Currency = CurrencyCode.Vnd,
                Status = "ACTIVE",
                Balance = 0m,
                LockedBalance = 0m
            };
            _context.Wallets.Add(wallet);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new WalletResponse(
            wallet.Id.ToString(),
            wallet.UserId.ToString(),
            wallet.Balance,
            wallet.AvailableBalance,
            wallet.LockedBalance,
            wallet.Currency.ToString(),
            wallet.Status,
            wallet.UpdatedAt
        );
    }

    public async Task<TopupResponse> TopupVnPayAsync(
        string userId,
        TopupVnPayRequest request,
        string clientIp,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(userId, out var userGuid) || userGuid == Guid.Empty)
            throw new ArgumentException("User ID must be a valid UUID.", nameof(userId));

        var wallet = await _context.Wallets.SingleOrDefaultAsync(w => w.UserId == userGuid, cancellationToken)
                     ?? throw new WalletNotFoundException(userId);

        var reference = $"TOPUP-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var paymentUrl = _vnPay.CreatePaymentUrl(
            reference,
            request.Amount,
            clientIp,
            request.BankCode,
            request.ReturnUrl);

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
            UserId = userGuid.ToString(),
            Action = AuditAction.TopupRequest,
            EntityName = nameof(WalletTopup),
            EntityId = topup.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new { topup.VnpTxnRef, topup.Amount, topup.Status })
        });

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new TopupResponse(
            topup.Id.ToString(),
            paymentUrl,
            topup.Amount,
            wallet.Currency.ToString(),
            topup.Status.ToString(),
            topup.CreatedAt
        );
    }

    public Task<WithdrawalResponse> WithdrawAsync(string userId, WithdrawalRequest request, CancellationToken cancellationToken = default)
    {
        throw new BusinessRuleValidationException(
            "Direct wallet withdrawal is disabled. Submit a withdrawal request for staff approval.");
    }

    public async Task<TransferResponse> TransferAsync(string senderUserId, TransferRequest request, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(senderUserId, out var senderGuid) || senderGuid == Guid.Empty)
            throw new ArgumentException("Sender user ID is invalid.", nameof(senderUserId));
        if (!Guid.TryParse(request.RecipientUserId, out var recipientGuid) || recipientGuid == Guid.Empty)
            throw new ArgumentException("Recipient user ID is invalid.", nameof(request.RecipientUserId));
        if (senderGuid == recipientGuid)
            throw new BusinessRuleValidationException("Cannot transfer funds to yourself.");

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var firstUserId = senderGuid.CompareTo(recipientGuid) < 0 ? senderGuid : recipientGuid;
        var secondUserId = firstUserId == senderGuid ? recipientGuid : senderGuid;
        var firstWallet = await _context.LockWalletByUserIdAsync(firstUserId, cancellationToken);
        var secondWallet = await _context.LockWalletByUserIdAsync(secondUserId, cancellationToken);
        var senderWallet = firstUserId == senderGuid ? firstWallet : secondWallet;
        var recipientWallet = firstUserId == recipientGuid ? firstWallet : secondWallet;

        if (senderWallet is null)
            throw new WalletNotFoundException(senderUserId);
        if (recipientWallet is null)
            throw new WalletNotFoundException(request.RecipientUserId);
        if (senderWallet.Currency != recipientWallet.Currency)
            throw new BusinessRuleValidationException("Wallet currencies do not match.");

        senderWallet.Deduct(request.Amount);
        recipientWallet.Deposit(request.Amount);

        var senderTx = new WalletTransaction
        {
            WalletId = senderWallet.Id,
            UserId = senderWallet.UserId.ToString(),
            Type = WalletTransactionType.TransferOut,
            Amount = request.Amount,
            Currency = senderWallet.Currency.ToString(),
            BalanceBefore = senderWallet.Balance + request.Amount,
            BalanceAfter = senderWallet.Balance,
            Status = TransactionStatus.Success,
            Note = string.IsNullOrWhiteSpace(request.Note) ? $"Transfer to {recipientGuid}" : request.Note.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.WalletTransactions.Add(senderTx);

        var recipientTx = new WalletTransaction
        {
            WalletId = recipientWallet.Id,
            UserId = recipientWallet.UserId.ToString(),
            Type = WalletTransactionType.TransferIn,
            Amount = request.Amount,
            Currency = recipientWallet.Currency.ToString(),
            BalanceBefore = recipientWallet.Balance - request.Amount,
            BalanceAfter = recipientWallet.Balance,
            Status = TransactionStatus.Success,
            Note = string.IsNullOrWhiteSpace(request.Note) ? $"Transfer from {senderGuid}" : request.Note.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.WalletTransactions.Add(recipientTx);

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new TransferResponse(
            senderTx.Id.ToString(),
            senderTx.Amount,
            senderTx.Currency,
            senderTx.Status.ToString(),
            senderTx.CreatedAt
        );
    }

    public async Task<WalletTransactionPageResponse> ListTransactionsAsync(
        string userId,
        ListTransactionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(userId, out var userGuid) || userGuid == Guid.Empty)
            throw new ArgumentException("User ID must be a valid UUID.", nameof(userId));

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 10 : Math.Min(request.PageSize, 100);

        var query = _context.WalletTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId);

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            var types = request.Type.Trim().ToUpperInvariant() switch
            {
                "ALL" => Array.Empty<WalletTransactionType>(),
                "DEPOSIT" => new[] { WalletTransactionType.TopupVnpay },
                "WITHDRAWAL" => new[]
                {
                    WalletTransactionType.WithdrawalDebit,
                    WalletTransactionType.WithdrawalReversal
                },
                "PAYMENT" => new[]
                {
                    WalletTransactionType.PurchaseDebit,
                    WalletTransactionType.DirectGatewayPayment
                },
                "REFUND" => new[] { WalletTransactionType.RefundCredit },
                _ when Enum.TryParse<WalletTransactionType>(request.Type.Trim(), true, out var txType) =>
                    new[] { txType },
                _ => throw new ArgumentException("Transaction type is invalid.", nameof(request.Type))
            };

            if (types.Length > 0)
                query = query.Where(t => types.Contains(t.Type));
        }

        if (request.FromDate.HasValue)
            query = query.Where(t => t.CreatedAt >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(t => t.CreatedAt <= request.ToDate.Value);

        var total = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling((double)total / pageSize);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new WalletTransactionResponse(
                t.Id.ToString(),
                t.WalletId.ToString(),
                t.UserId ?? string.Empty,
                t.Type.ToString(),
                t.Amount,
                t.Currency ?? "VND",
                t.BalanceAfter,
                t.Status.ToString(),
                t.Note ?? string.Empty,
                t.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return new WalletTransactionPageResponse(items, total, page, pageSize, totalPages);
    }

    public async Task<DirectPaymentResponse> CheckoutDirectAsync(
        string userId,
        DirectPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var buyerId = Guid.Parse(userId);
        var orderId = Guid.Parse(request.OrderId);
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(buyerId, cancellationToken)
                     ?? throw new WalletNotFoundException(userId);

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
                existingTransaction.Status.ToString().ToUpperInvariant(),
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
            transaction.Status.ToString().ToUpperInvariant(),
            transaction.CreatedAt
        );
    }

    public async Task<VnPayIpnResponse> ProcessVnPayIpnAsync(ProcessVnPayIpnRequest request, CancellationToken cancellationToken = default)
    {
        if (!_vnPay.ValidateSignature(request.Parameters))
        {
            _logger.LogWarning("Invalid VNPay signature on IPN payload.");
            return new VnPayIpnResponse("97", "Invalid signature");
        }

        request.Parameters.TryGetValue("vnp_TxnRef", out var txnRef);
        request.Parameters.TryGetValue("vnp_ResponseCode", out var responseCode);
        request.Parameters.TryGetValue("vnp_Amount", out var rawAmount);
        request.Parameters.TryGetValue("vnp_TransactionNo", out var vnpayTransactionNo);

        if (string.IsNullOrWhiteSpace(txnRef) || string.IsNullOrWhiteSpace(responseCode))
        {
            return new VnPayIpnResponse("99", "Missing parameters");
        }

        var topup = await _context.WalletTopups.FirstOrDefaultAsync(t => t.VnpTxnRef == txnRef, cancellationToken);
        if (topup == null)
        {
            return new VnPayIpnResponse("01", "Order not found");
        }

        if (topup.Status != TopupStatus.Pending)
        {
            return new VnPayIpnResponse("02", "Order already processed");
        }

        if (decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedAmount))
        {
            if (topup.Amount * 100 != parsedAmount)
            {
                return new VnPayIpnResponse("04", "Invalid amount");
            }
        }

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (responseCode == "00")
        {
            topup.Status = TopupStatus.Success;
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == topup.WalletId, cancellationToken);
            if (wallet != null)
            {
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
            }
        }
        else
        {
            topup.Status = TopupStatus.Failed;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new VnPayIpnResponse("00", "Success");
    }
}
