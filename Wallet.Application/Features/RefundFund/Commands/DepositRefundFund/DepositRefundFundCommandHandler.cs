namespace Wallet.Application.Features.RefundFund.Commands.DepositRefundFund;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class DepositRefundFundCommandHandler : IRequestHandler<DepositRefundFundCommand, RefundFundDepositResult>
{
    private readonly IWalletDbContext _context;

    public DepositRefundFundCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<RefundFundDepositResult> Handle(DepositRefundFundCommand request, CancellationToken cancellationToken)
    {
        if (request.AdminUserId == Guid.Empty)
            throw new BusinessRuleValidationException("FORBIDDEN", "The admin could not be identified.", 403);

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var fund = await _context.LockOrEnsureWalletByUserIdAsync(RefundFundAccount.UserId, cancellationToken);
        fund.EnsureActive();

        var before = fund.Balance;
        fund.Deposit(request.Amount);
        var transaction = new WalletTransaction
        {
            WalletId = fund.Id,
            UserId = fund.UserId.ToString(),
            Type = WalletTransactionType.RefundFundDeposit,
            Amount = request.Amount,
            Currency = fund.Currency.ToString(),
            BalanceBefore = before,
            BalanceAfter = fund.Balance,
            Status = TransactionStatus.Success,
            Note = $"Refund fund deposit by admin {request.AdminUserId}: {request.EvidenceRef.Trim()}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.WalletTransactions.Add(transaction);
        _context.AddAuditLog(
            action: AuditAction.BalanceAdjustment,
            entityName: nameof(WalletAccount),
            entityId: fund.Id.ToString(),
            newValues: new { Type = "RefundFundDeposit", request.Amount, fund.Balance, request.EvidenceRef },
            userId: request.AdminUserId.ToString());

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return new RefundFundDepositResult(transaction.Id.ToString(), request.Amount, fund.Balance);
    }
}
