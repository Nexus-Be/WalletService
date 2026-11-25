namespace Wallet.Tests;

using System;
using System.Threading.Tasks;
using Wallet.Application.Features.Wallets.Queries.GetTransactionById;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class GetTransactionByIdTests
{
    [Fact]
    public async Task GetTransactionById_ReturnsDetailForOwner_And404ForOthers()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            Balance = 350_000m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        };
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 150_000m,
            VnpTransactionNo = "pi_test_123",
            Status = TopupStatus.Success
        };
        var tx = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = ownerId.ToString(),
            Type = WalletTransactionType.TopupStripe,
            Amount = 150_000m,
            BalanceBefore = 200_000m,
            BalanceAfter = 350_000m,
            Currency = "Vnd",
            Status = TransactionStatus.Success,
            ReferenceId = topup.Id,
            Note = "Stripe topup"
        };
        context.Wallets.Add(wallet);
        context.WalletTopups.Add(topup);
        context.WalletTransactions.Add(tx);
        await context.SaveChangesAsync();

        var handler = new GetTransactionByIdQueryHandler(context);
        var detail = await handler.Handle(new GetTransactionByIdQuery(tx.Id) { UserId = ownerId }, default);

        Assert.Equal(tx.Id.ToString(), detail.TransactionId);
        Assert.Equal(200_000m, detail.BalanceBefore);
        Assert.Equal(350_000m, detail.BalanceAfter);
        Assert.Equal("Success", detail.GatewayStatus);
        Assert.Equal("pi_test_123", detail.GatewayReference);

        var notFoundOther = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new GetTransactionByIdQuery(tx.Id) { UserId = otherUserId }, default));
        Assert.Equal("TRANSACTION_NOT_FOUND", notFoundOther.ErrorCode);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new GetTransactionByIdQuery(Guid.NewGuid()) { UserId = ownerId }, default));
    }

    [Fact]
    public async Task GetTransactionById_ResolvesGatewayDetailsAcrossDirectWithdrawalTransferEscrowAndAuctionTypes()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var ownerId = Guid.NewGuid();
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            Balance = 1_000_000m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        };

        var stripeTopup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 100_000m,
            Status = TopupStatus.Success
        };
        var stripeTopupAttempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Purpose = StripePaymentPurpose.Topup,
            WalletTopupId = stripeTopup.Id,
            Amount = 100_000m,
            RequestFingerprint = "fp-topup",
            ProviderIdempotencyKey = "pk-topup",
            PaymentIntentId = "pi_topup_clean",
            Status = StripePaymentAttemptStatus.Successful
        };

        var directOrderId = Guid.NewGuid();
        var directOrder = new DirectPaymentOrder
        {
            Id = Guid.NewGuid(),
            OrderId = directOrderId,
            Amount = 80_000m,
            Status = TopupStatus.Success
        };
        var directAttempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Purpose = StripePaymentPurpose.DirectOrder,
            DirectPaymentOrderId = directOrder.Id,
            Amount = 80_000m,
            RequestFingerprint = "fp-direct",
            ProviderIdempotencyKey = "pk-direct",
            PaymentIntentId = "pi_direct_clean",
            Status = StripePaymentAttemptStatus.Successful
        };

        var withdrawal = new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 120_000m,
            NetAmount = 120_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Owner",
            Status = WithdrawalStatus.Completed,
            GatewayReferenceId = "BANK-FT-777"
        };

        var holdCapture = new AuctionHold
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            OwnerUserId = ownerId,
            ReferenceType = "AUCTION_DEPOSIT",
            ReferenceId = "auction-cap-1",
            Amount = 50_000m,
            MetadataJson = "{}",
            Status = AuctionHoldStatus.Captured
        };
        var holdRefund = new AuctionHold
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            OwnerUserId = ownerId,
            ReferenceType = "AUCTION_DEPOSIT",
            ReferenceId = "auction-ref-2",
            Amount = 40_000m,
            MetadataJson = "{}",
            Status = AuctionHoldStatus.Released
        };

        var topupTx = NewTx(wallet, WalletTransactionType.TopupStripe, 100_000m, 0m, 100_000m, stripeTopup.Id, "Stripe topup");
        var directTx = NewTx(wallet, WalletTransactionType.DirectGatewayPayment, 80_000m, 100_000m, 100_000m, directOrderId, "Stripe direct");
        var wdDebitTx = NewTx(wallet, WalletTransactionType.WithdrawalDebit, 120_000m, 220_000m, 100_000m, withdrawal.Id, "Withdrawal debit");
        var wdRevTx = NewTx(wallet, WalletTransactionType.WithdrawalReversal, 120_000m, 100_000m, 220_000m, withdrawal.Id, "Withdrawal reversal");
        var transferRef = Guid.NewGuid();
        var transferOutTx = NewTx(wallet, WalletTransactionType.TransferOut, 25_000m, 220_000m, 195_000m, transferRef, "Rent");
        var transferInTx = NewTx(wallet, WalletTransactionType.TransferIn, 25_000m, 195_000m, 220_000m, transferRef, "Rent in");
        var escrowCancelTx = NewTx(wallet, WalletTransactionType.EscrowCancellationRefund, 60_000m, 220_000m, 280_000m, Guid.NewGuid(), "Escrow cancelled");
        var holdCaptureTx = NewTx(wallet, WalletTransactionType.AuctionHoldCapture, 50_000m, 280_000m, 230_000m, holdCapture.Id, "Auction win capture");
        var holdRefundTx = NewTx(wallet, WalletTransactionType.AuctionDepositRefund, 40_000m, 230_000m, 270_000m, holdRefund.Id, "Auction outbid refund");

        context.Wallets.Add(wallet);
        context.WalletTopups.Add(stripeTopup);
        context.DirectPaymentOrders.Add(directOrder);
        context.StripePaymentAttempts.AddRange(stripeTopupAttempt, directAttempt);
        context.WithdrawalRequests.Add(withdrawal);
        context.AuctionHolds.AddRange(holdCapture, holdRefund);
        context.WalletTransactions.AddRange(
            topupTx, directTx, wdDebitTx, wdRevTx, transferOutTx, transferInTx, escrowCancelTx, holdCaptureTx, holdRefundTx);
        await context.SaveChangesAsync();

        var handler = new GetTransactionByIdQueryHandler(context);

        var topupRes = await handler.Handle(new GetTransactionByIdQuery(topupTx.Id) { UserId = ownerId }, default);
        Assert.Equal("Success", topupRes.GatewayStatus);
        Assert.Equal("pi_topup_clean", topupRes.GatewayReference);

        var directRes = await handler.Handle(new GetTransactionByIdQuery(directTx.Id) { UserId = ownerId }, default);
        Assert.Equal("Success", directRes.GatewayStatus);
        Assert.Equal("pi_direct_clean", directRes.GatewayReference);

        var wdDebitRes = await handler.Handle(new GetTransactionByIdQuery(wdDebitTx.Id) { UserId = ownerId }, default);
        Assert.Equal("Completed", wdDebitRes.GatewayStatus);
        Assert.Equal("BANK-FT-777", wdDebitRes.GatewayReference);

        var wdRevRes = await handler.Handle(new GetTransactionByIdQuery(wdRevTx.Id) { UserId = ownerId }, default);
        Assert.Equal("Completed", wdRevRes.GatewayStatus);
        Assert.Equal("BANK-FT-777", wdRevRes.GatewayReference);

        var transferOutRes = await handler.Handle(new GetTransactionByIdQuery(transferOutTx.Id) { UserId = ownerId }, default);
        Assert.Null(transferOutRes.GatewayStatus);
        Assert.Null(transferOutRes.GatewayReference);
        Assert.Equal(220_000m, transferOutRes.BalanceBefore);
        Assert.Equal(195_000m, transferOutRes.BalanceAfter);

        var transferInRes = await handler.Handle(new GetTransactionByIdQuery(transferInTx.Id) { UserId = ownerId }, default);
        Assert.Null(transferInRes.GatewayStatus);
        Assert.Null(transferInRes.GatewayReference);

        var escrowCancelRes = await handler.Handle(new GetTransactionByIdQuery(escrowCancelTx.Id) { UserId = ownerId }, default);
        Assert.Null(escrowCancelRes.GatewayStatus);
        Assert.Null(escrowCancelRes.GatewayReference);

        var captureRes = await handler.Handle(new GetTransactionByIdQuery(holdCaptureTx.Id) { UserId = ownerId }, default);
        Assert.Equal("Captured", captureRes.GatewayStatus);
        Assert.Equal("auction-cap-1", captureRes.GatewayReference);

        var refundRes = await handler.Handle(new GetTransactionByIdQuery(holdRefundTx.Id) { UserId = ownerId }, default);
        Assert.Equal("Released", refundRes.GatewayStatus);
        Assert.Equal("auction-ref-2", refundRes.GatewayReference);
    }

    private static WalletTransaction NewTx(
        WalletAccount wallet,
        WalletTransactionType type,
        decimal amount,
        decimal before,
        decimal after,
        Guid referenceId,
        string note) => new()
    {
        Id = Guid.NewGuid(),
        WalletId = wallet.Id,
        UserId = wallet.UserId.ToString(),
        Type = type,
        Amount = amount,
        BalanceBefore = before,
        BalanceAfter = after,
        Currency = "Vnd",
        Status = TransactionStatus.Success,
        ReferenceId = referenceId,
        Note = note
    };
}
