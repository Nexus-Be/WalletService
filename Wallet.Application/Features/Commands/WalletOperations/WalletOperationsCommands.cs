namespace Wallet.Application.Features.Commands.WalletOperations;

using System.Globalization;
using System.Text.Json;
using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.DTOs;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Events;
using Wallet.Domain.Exceptions;

public sealed record InitiateDirectGatewayPaymentCommand(
    string BuyerId,
    string OrderId,
    decimal Amount,
    string Gateway) : IRequest<DirectGatewayPaymentDto>;

public sealed record DirectGatewayPaymentDto(string PaymentUrl, string TransactionId, string Status);

public sealed class
    InitiateDirectGatewayPaymentCommandValidator : AbstractValidator<InitiateDirectGatewayPaymentCommand>
{
    public InitiateDirectGatewayPaymentCommandValidator()
    {
        RuleFor(x => x.BuyerId).Must(x => Guid.TryParse(x, out var userId) && userId != Guid.Empty);
        RuleFor(x => x.OrderId).Must(x => Guid.TryParse(x, out var orderId) && orderId != Guid.Empty);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Amount).Must(x => decimal.Round(x, 2) == x);
        RuleFor(x => x.Gateway).Matches("^[A-Za-z0-9_-]{1,50}$");
    }
}

public sealed class InitiateDirectGatewayPaymentCommandHandler
    : IRequestHandler<InitiateDirectGatewayPaymentCommand, DirectGatewayPaymentDto>
{
    private readonly IWalletDbContext _context;
    private readonly IDirectPaymentGateway _paymentGateway;
    private readonly IPublishEndpoint _publishEndpoint;

    public InitiateDirectGatewayPaymentCommandHandler(
        IWalletDbContext context,
        IDirectPaymentGateway paymentGateway,
        IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<DirectGatewayPaymentDto> Handle(
        InitiateDirectGatewayPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var buyerId = Guid.Parse(request.BuyerId);
        var orderId = Guid.Parse(request.OrderId);
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(buyerId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.BuyerId);

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
                    $"Thanh toán đơn hàng qua {request.Gateway.Trim()}",
                    StringComparison.Ordinal))
                throw new BusinessRuleValidationException("Order already has a direct payment transaction.");

            var existingPaymentUrl = _paymentGateway.CreatePaymentUrl(
                request.Gateway.Trim(),
                orderId,
                existingTransaction.Amount,
                wallet.Currency.ToString());
            await dbTransaction.CommitAsync(cancellationToken);
            return new DirectGatewayPaymentDto(
                existingPaymentUrl,
                existingTransaction.Id.ToString(),
                existingTransaction.Status.ToString().ToUpperInvariant());
        }

        string paymentUrl;
        try
        {
            paymentUrl = _paymentGateway.CreatePaymentUrl(
                request.Gateway.Trim(),
                orderId,
                request.Amount,
                wallet.Currency.ToString());
        }
        catch (InvalidOperationException)
        {
            await _publishEndpoint.Publish<IDirectPaymentFailedEvent>(new
            {
                OrderId = orderId.ToString(),
                Gateway = request.Gateway.Trim(),
                Reason = "Payment gateway is not available.",
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            throw new BusinessRuleValidationException("PAYMENT_GATEWAY_UNAVAILABLE");
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
            ReferenceId = orderId,
            Status = TransactionStatus.Pending,
            Note = $"Thanh toán đơn hàng qua {request.Gateway.Trim()}"
        };
        _context.WalletTransactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);
        await _publishEndpoint.Publish<IDirectPaymentInitiatedEvent>(new
        {
            OrderId = orderId.ToString(),
            TransactionId = transaction.Id.ToString(),
            Gateway = request.Gateway.Trim(),
            Amount = request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new DirectGatewayPaymentDto(paymentUrl, transaction.Id.ToString(), "PENDING");
    }
}

public sealed record RequestWithdrawalCommand(
    string SellerId,
    decimal Amount,
    string BankName,
    string BankAccountNumber,
    string BankAccountHolder) : IRequest<WithdrawalDto>;

public sealed record WithdrawalDto(
    string WithdrawalId,
    string Status,
    decimal NetAmount,
    decimal Fee,
    string? ProcessedBy = null);

public sealed class RequestWithdrawalCommandValidator : AbstractValidator<RequestWithdrawalCommand>
{
    public RequestWithdrawalCommandValidator()
    {
        RuleFor(x => x.SellerId).Must(x => Guid.TryParse(x, out var userId) && userId != Guid.Empty);
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(50_000m);
        RuleFor(x => x.Amount).Must(x => decimal.Round(x, 2) == x);
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BankAccountNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.BankAccountHolder).NotEmpty().MaximumLength(150);
    }
}

public sealed class RequestWithdrawalCommandHandler : IRequestHandler<RequestWithdrawalCommand, WithdrawalDto>
{
    private const decimal WithdrawalFee = 5_000m;
    private readonly IWalletDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;

    public RequestWithdrawalCommandHandler(IWalletDbContext context, IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<WithdrawalDto> Handle(RequestWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var sellerId = Guid.Parse(request.SellerId);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(sellerId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.SellerId);

        wallet.LockFunds(request.Amount);
        var withdrawal = new WithdrawalRequest
        {
            WalletId = wallet.Id,
            Amount = request.Amount,
            Fee = WithdrawalFee,
            NetAmount = request.Amount - WithdrawalFee,
            BankName = request.BankName.Trim(),
            BankAccountNumber = request.BankAccountNumber.Trim(),
            BankAccountHolder = request.BankAccountHolder.Trim(),
            Status = WithdrawalStatus.Pending
        };

        _context.WithdrawalRequests.Add(withdrawal);
        _context.BankAccounts.Add(new BankAccount
        {
            UserId = sellerId.ToString(),
            BankName = withdrawal.BankName,
            AccountNumber = withdrawal.BankAccountNumber,
            AccountHolderName = withdrawal.BankAccountHolder
        });
        _context.AuditLogs.Add(new AuditLog
        {
            ServiceName = "Wallet.Api",
            UserId = sellerId.ToString(),
            Action = AuditAction.WithdrawalRequest,
            EntityName = nameof(WithdrawalRequest),
            EntityId = withdrawal.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new
            {
                withdrawal.Amount,
                withdrawal.NetAmount,
                withdrawal.Fee,
                withdrawal.Status
            })
        });

        await _context.SaveChangesAsync(cancellationToken);
        await _publishEndpoint.Publish<IWithdrawalRequestedEvent>(new
        {
            WithdrawalId = withdrawal.Id.ToString(),
            WalletId = wallet.Id.ToString(),
            Amount = withdrawal.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            NetAmount = withdrawal.NetAmount.ToString("0.00", CultureInfo.InvariantCulture),
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WithdrawalDto(
            withdrawal.Id.ToString(),
            withdrawal.Status.ToString().ToUpperInvariant(),
            withdrawal.NetAmount,
            withdrawal.Fee);
    }
}

public sealed record ProcessWithdrawalCommand(
    string WithdrawalId,
    string AdminId,
    string Action,
    string? RejectReason) : IRequest<WithdrawalDto>;

public sealed class ProcessWithdrawalCommandValidator : AbstractValidator<ProcessWithdrawalCommand>
{
    public ProcessWithdrawalCommandValidator()
    {
        RuleFor(x => x.WithdrawalId).Must(x => Guid.TryParse(x, out var withdrawalId) && withdrawalId != Guid.Empty);
        RuleFor(x => x.AdminId).Must(x => Guid.TryParse(x, out var adminId) && adminId != Guid.Empty);
        RuleFor(x => x.Action).Must(x =>
            string.Equals(x, "APPROVE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x, "REJECT", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.RejectReason)
            .NotEmpty()
            .When(x => string.Equals(x.Action, "REJECT", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ProcessWithdrawalCommandHandler : IRequestHandler<ProcessWithdrawalCommand, WithdrawalDto>
{
    private readonly IWalletDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;

    public ProcessWithdrawalCommandHandler(IWalletDbContext context, IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<WithdrawalDto> Handle(ProcessWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var withdrawalId = Guid.Parse(request.WithdrawalId);
        var adminId = Guid.Parse(request.AdminId);
        var approve = string.Equals(request.Action, "APPROVE", StringComparison.OrdinalIgnoreCase);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var withdrawal = await _context.LockWithdrawalRequestAsync(withdrawalId, cancellationToken);

        if (withdrawal is null)
            throw new EntityNotFoundException(nameof(WithdrawalRequest), withdrawalId);
        if (withdrawal.Status != WithdrawalStatus.Pending)
            throw new BusinessRuleValidationException("WITHDRAWAL_ALREADY_PROCESSED");

        var wallet = await _context.LockWalletByIdAsync(withdrawal.WalletId, cancellationToken)
                     ?? throw new WalletNotFoundException(withdrawal.WalletId.ToString());

        var oldStatus = withdrawal.Status;
        if (approve)
        {
            wallet.CompleteLockedWithdrawal(withdrawal.Amount);
            withdrawal.Status = WithdrawalStatus.Approved;
            withdrawal.ProcessedAt = DateTimeOffset.UtcNow;
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = wallet.Id,
                UserId = wallet.UserId.ToString(),
                Type = WalletTransactionType.WithdrawalDebit,
                Amount = withdrawal.Amount,
                Currency = wallet.Currency.ToString(),
                BalanceBefore = wallet.Balance + withdrawal.Amount,
                BalanceAfter = wallet.Balance,
                ReferenceId = withdrawal.Id,
                Status = TransactionStatus.Success,
                Note =
                    $"Rút tiền tới {withdrawal.BankName}, {withdrawal.BankAccountNumber}; phí {withdrawal.Fee.ToString("0.00", CultureInfo.InvariantCulture)}"
            });
        }
        else
        {
            wallet.ReleaseLockedFunds(withdrawal.Amount);
            withdrawal.Status = WithdrawalStatus.Rejected;
            withdrawal.ProcessedAt = DateTimeOffset.UtcNow;
        }

        withdrawal.ProcessedBy = adminId;
        _context.AuditLogs.Add(new AuditLog
        {
            ServiceName = "Wallet.Api",
            UserId = adminId.ToString(),
            Action = approve ? AuditAction.WithdrawalApprove : AuditAction.WithdrawalReject,
            EntityName = nameof(WithdrawalRequest),
            EntityId = withdrawal.Id.ToString(),
            OldValues = JsonSerializer.Serialize(new { Status = oldStatus.ToString() }),
            NewValues = JsonSerializer.Serialize(new
            {
                Status = withdrawal.Status.ToString(),
                withdrawal.ProcessedBy,
                Reason = approve ? null : request.RejectReason
            })
        });

        await _context.SaveChangesAsync(cancellationToken);
        if (approve)
        {
            await _publishEndpoint.Publish<IWithdrawalApprovedEvent>(new
            {
                WithdrawalId = withdrawal.Id.ToString(),
                WalletId = wallet.Id.ToString(),
                Amount = withdrawal.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                NetAmount = withdrawal.NetAmount.ToString("0.00", CultureInfo.InvariantCulture),
                ProcessedBy = adminId.ToString(),
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        else
        {
            await _publishEndpoint.Publish<IWithdrawalRejectedEvent>(new
            {
                WithdrawalId = withdrawal.Id.ToString(),
                WalletId = wallet.Id.ToString(),
                Reason = request.RejectReason ?? string.Empty,
                ProcessedBy = adminId.ToString(),
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WithdrawalDto(
            withdrawal.Id.ToString(),
            withdrawal.Status.ToString().ToUpperInvariant(),
            withdrawal.NetAmount,
            withdrawal.Fee,
            adminId.ToString());
    }
}

public sealed record AdjustWalletBalanceCommand(
    string UserId,
    string AdminId,
    decimal Amount,
    string Action,
    string Reason) : IRequest<WalletTransactionDto>;

public sealed class AdjustWalletBalanceCommandValidator : AbstractValidator<AdjustWalletBalanceCommand>
{
    public AdjustWalletBalanceCommandValidator()
    {
        RuleFor(x => x.UserId).Must(x => Guid.TryParse(x, out var userId) && userId != Guid.Empty);
        RuleFor(x => x.AdminId).Must(x => Guid.TryParse(x, out var adminId) && adminId != Guid.Empty);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Amount).Must(x => decimal.Round(x, 2) == x);
        RuleFor(x => x.Action).Must(x =>
            string.Equals(x, "CREDIT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x, "DEBIT", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class
    AdjustWalletBalanceCommandHandler : IRequestHandler<AdjustWalletBalanceCommand, WalletTransactionDto>
{
    private readonly IWalletDbContext _context;

    public AdjustWalletBalanceCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletTransactionDto> Handle(
        AdjustWalletBalanceCommand request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(request.UserId);
        var adminId = Guid.Parse(request.AdminId);
        var credit = string.Equals(request.Action, "CREDIT", StringComparison.OrdinalIgnoreCase);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(userId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.UserId);

        var balanceBefore = wallet.Balance;
        if (credit)
            wallet.Deposit(request.Amount);
        else
            wallet.Deduct(request.Amount);

        var walletTransaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = credit ? WalletTransactionType.AdjustmentCredit : WalletTransactionType.AdjustmentDebit,
            Amount = request.Amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            Note = $"Admin adjustment ({request.Action}): {request.Reason.Trim()}"
        };
        _context.WalletTransactions.Add(walletTransaction);
        _context.AuditLogs.Add(new AuditLog
        {
            ServiceName = "Wallet.Api",
            UserId = adminId.ToString(),
            Action = AuditAction.BalanceAdjustment,
            EntityName = nameof(WalletAccount),
            EntityId = wallet.Id.ToString(),
            OldValues = JsonSerializer.Serialize(new { Balance = balanceBefore }),
            NewValues = JsonSerializer.Serialize(new { Balance = wallet.Balance, request.Action, request.Amount }),
            ChangedColumns = JsonSerializer.Serialize(new[] { nameof(WalletAccount.Balance) })
        });

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WalletTransactionDto
        {
            Id = walletTransaction.Id.ToString(),
            WalletId = wallet.Id.ToString(),
            UserId = wallet.UserId.ToString(),
            Type = walletTransaction.Type.ToString(),
            Amount = walletTransaction.Amount,
            Currency = walletTransaction.Currency ?? "VND",
            BalanceAfter = walletTransaction.BalanceAfter,
            Status = walletTransaction.Status.ToString(),
            Note = walletTransaction.Note ?? string.Empty,
            CreatedAt = walletTransaction.CreatedAt
        };
    }
}