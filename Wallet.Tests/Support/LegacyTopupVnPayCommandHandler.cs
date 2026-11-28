namespace Wallet.Application.Features.Topups.Commands.TopupVnPay;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class TopupVnPayCommandHandler : IRequestHandler<TopupVnPayCommand, TopupResponse>
{
    private readonly IWalletDbContext _context;
    private readonly IVnPayService _vnPay;

    public TopupVnPayCommandHandler(IWalletDbContext context, IVnPayService vnPay)
    {
        _context = context;
        _vnPay = vnPay;
    }

    public async Task<TopupResponse> Handle(TopupVnPayCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userGuid) || userGuid == Guid.Empty)
            throw new ArgumentException("User ID must be a valid UUID.", nameof(request));

        var wallet = await _context.Wallets.SingleOrDefaultAsync(w => w.UserId == userGuid, cancellationToken)
                     ?? throw new WalletNotFoundException(request.UserId);

        var reference = $"TOPUP-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var paymentUrl = _vnPay.CreatePaymentUrl(
            reference,
            request.Amount,
            request.ClientIp,
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
        _context.AddAuditLog(
            action: AuditAction.TopupRequest,
            entityName: nameof(WalletTopup),
            entityId: topup.Id.ToString(),
            newValues: new { topup.VnpTxnRef, topup.Amount, topup.Status },
            userId: userGuid.ToString());

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
}
