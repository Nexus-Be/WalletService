namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Api.Controllers;
using Wallet.Api.Filters;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.AdminWallets.Queries.GetWalletReconciliation;
using Wallet.Application.Features.Checkout.Commands.AdjustOrderPayment;
using Wallet.Application.Features.Checkout.Commands.CancelOrderEscrow;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Application.Features.Wallets.Queries.ListTransactions;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class CancelOrderEscrowTests
{
    [Fact]
    public async Task CancelOrderEscrow_HappyPath_ReturnsFullBalance_UpdatesEscrow_LogsAudit_AndReconcilesBalanced()
    {
        using var context = new TestWalletDbContext(nameof(CancelOrderEscrow_HappyPath_ReturnsFullBalance_UpdatesEscrow_LogsAudit_AndReconcilesBalanced));
        var buyer = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();

        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(200_000m) { OrderId = orderId, UserId = buyer.UserId.ToString() },
            default);

        var result = await new CancelOrderEscrowCommandHandler(context).Handle(
            new CancelOrderEscrowCommand("Order cancelled before fulfillment", buyer.UserId.ToString(), 200_000m)
            {
                OrderId = orderId,
                IdempotencyKey = "cancel-escrow-1"
            },
            default);

        Assert.False(result.IsReplay);
        Assert.Equal(orderId.ToString(), result.OrderId);
        Assert.Equal(buyer.Id.ToString(), result.WalletId);
        Assert.Equal(buyer.UserId.ToString(), result.UserId);
        Assert.Equal("EscrowCancellationRefund", result.Type);
        Assert.Equal(200_000m, result.ReturnedAmount);
        Assert.Equal("Vnd", result.Currency);
        Assert.Equal(300_000m, result.BalanceBefore);
        Assert.Equal(500_000m, result.BalanceAfter);
        Assert.Equal("Cancelled", result.EscrowStatus);
        Assert.Equal("Success", result.Status);
        Assert.Equal("Order escrow cancelled: Order cancelled before fulfillment", result.Note);

        Assert.Equal(500_000m, buyer.Balance);

        var escrow = context.OrderEscrows.Single(item => item.OrderId == orderId);
        Assert.Equal(OrderEscrowStatus.Cancelled, escrow.Status);
        Assert.Equal(0m, escrow.RemainingAmount);
        Assert.Equal(200_000m, escrow.OriginalAmount);
        Assert.NotNull(escrow.CancelledAt);
        Assert.Equal("Order cancelled before fulfillment", escrow.CancelReason);

        var tx = context.WalletTransactions.Single(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.EscrowCancellationRefund);
        Assert.Equal(tx.Id.ToString(), result.TransactionId);
        Assert.Equal(300_000m, tx.BalanceBefore);
        Assert.Equal(500_000m, tx.BalanceAfter);
        Assert.Equal("cancel-escrow-1", tx.ClientIdempotencyKey);
        Assert.False(string.IsNullOrWhiteSpace(tx.RequestFingerprint));

        var audit = context.AuditLogs.Single(item =>
            item.Action == AuditAction.OrderEscrowCancel && item.EntityId == orderId.ToString());
        Assert.Equal(buyer.UserId.ToString(), audit.UserId);
        Assert.Equal(nameof(OrderEscrow), audit.EntityName);
        Assert.NotNull(audit.OldValues);
        Assert.NotNull(audit.NewValues);
        Assert.NotNull(audit.ChangedColumns);
        Assert.Contains("Balance", audit.ChangedColumns);
        Assert.Contains("RemainingAmount", audit.ChangedColumns);
        Assert.Contains("Status", audit.ChangedColumns);
        Assert.Contains("CancelledAt", audit.ChangedColumns);
        Assert.Contains("CancelReason", audit.ChangedColumns);

        var reconciliation = await new GetWalletReconciliationQueryHandler(context).Handle(
            new GetWalletReconciliationQuery(WalletId: buyer.Id),
            default);
        var entry = Assert.Single(reconciliation.Items);
        Assert.Equal("Balanced", entry.Result);
        Assert.Equal(500_000m, entry.CurrentBalance);
        Assert.Equal(500_000m, entry.LatestSuccessfulBalance);
    }

    [Fact]
    public async Task CancelOrderEscrow_AfterPositiveAndNegativeAdjustments_ReturnsAdjustedAmount()
    {
        using var context = new TestWalletDbContext(nameof(CancelOrderEscrow_AfterPositiveAndNegativeAdjustments_ReturnsAdjustedAmount));
        var buyer = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();

        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(200_000m) { OrderId = orderId, UserId = buyer.UserId.ToString() },
            default);

        var adjustHandler = new AdjustOrderPaymentCommandHandler(context);
        await adjustHandler.Handle(
            new AdjustOrderPaymentCommand(buyer.UserId.ToString(), 30_000m, "Extra shipping")
            {
                OrderId = orderId,
                IdempotencyKey = "adj-plus-1"
            },
            default);
        await adjustHandler.Handle(
            new AdjustOrderPaymentCommand(buyer.UserId.ToString(), -10_000m, "Shipping discount")
            {
                OrderId = orderId,
                IdempotencyKey = "adj-minus-1"
            },
            default);

        var result = await new CancelOrderEscrowCommandHandler(context).Handle(
            new CancelOrderEscrowCommand("Buyer cancelled after shipping adjustment")
            {
                OrderId = orderId,
                IdempotencyKey = "cancel-after-adjust"
            },
            default);

        Assert.Equal(220_000m, result.ReturnedAmount);
        Assert.Equal(500_000m, buyer.Balance);
        Assert.Equal(OrderEscrowStatus.Cancelled, context.OrderEscrows.Single(item => item.OrderId == orderId).Status);
    }

    [Fact]
    public async Task CancelOrderEscrow_LegacyPaymentWithoutEscrowRow_ReconcilesAndCancelsAtomically()
    {
        using var context = new TestWalletDbContext(nameof(CancelOrderEscrow_LegacyPaymentWithoutEscrowRow_ReconcilesAndCancelsAtomically));
        var buyer = AddWallet(context, balance: 850_000m);
        var orderId = Guid.NewGuid();
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = buyer.Id,
            UserId = buyer.UserId.ToString(),
            Type = WalletTransactionType.PurchaseDebit,
            Amount = 150_000m,
            Currency = buyer.Currency.ToString(),
            BalanceBefore = 1_000_000m,
            BalanceAfter = 850_000m,
            Status = TransactionStatus.Success,
            ReferenceId = orderId
        });
        await context.SaveChangesAsync();

        var result = await new CancelOrderEscrowCommandHandler(context).Handle(
            new CancelOrderEscrowCommand("Cancel legacy order")
            {
                OrderId = orderId,
                IdempotencyKey = "cancel-legacy-1"
            },
            default);

        Assert.Equal(150_000m, result.ReturnedAmount);
        Assert.Equal(1_000_000m, buyer.Balance);
        Assert.Equal(OrderEscrowStatus.Cancelled, context.OrderEscrows.Single(item => item.OrderId == orderId).Status);
        Assert.Single(context.AuditLogs.Where(item => item.Action == AuditAction.OrderEscrowReconciliation));
        Assert.Single(context.AuditLogs.Where(item => item.Action == AuditAction.OrderEscrowCancel));
    }

    [Fact]
    public async Task CancelOrderEscrow_FrozenBuyerWallet_CreditsWalletAndRecordsFrozenStatusInAudit()
    {
        using var context = new TestWalletDbContext(nameof(CancelOrderEscrow_FrozenBuyerWallet_CreditsWalletAndRecordsFrozenStatusInAudit));
        var buyer = AddWallet(context, balance: 400_000m);
        var orderId = Guid.NewGuid();

        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = orderId, UserId = buyer.UserId.ToString() },
            default);
        buyer.Freeze();
        await context.SaveChangesAsync();

        var result = await new CancelOrderEscrowCommandHandler(context).Handle(
            new CancelOrderEscrowCommand("Dispute cancelled order")
            {
                OrderId = orderId,
                IdempotencyKey = "cancel-frozen-1"
            },
            default);

        Assert.Equal(400_000m, buyer.Balance);
        Assert.Equal(400_000m, result.BalanceAfter);
        var audit = context.AuditLogs.Single(item => item.Action == AuditAction.OrderEscrowCancel);
        Assert.Contains("FROZEN", audit.NewValues);
    }

    [Fact]
    public async Task CancelOrderEscrow_IdempotencyReplayAndKeyConflictBehaviors()
    {
        using var context = new TestWalletDbContext(nameof(CancelOrderEscrow_IdempotencyReplayAndKeyConflictBehaviors));
        var buyer = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();

        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(120_000m) { OrderId = orderId, UserId = buyer.UserId.ToString() },
            default);

        await new AdjustOrderPaymentCommandHandler(context).Handle(
            new AdjustOrderPaymentCommand(buyer.UserId.ToString(), 20_000m, "Adjust shipping")
            {
                OrderId = orderId,
                IdempotencyKey = "shared-key"
            },
            default);

        var handler = new CancelOrderEscrowCommandHandler(context);
        var crossOpConflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Cancel order")
            {
                OrderId = orderId,
                IdempotencyKey = "shared-key"
            },
            default));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", crossOpConflict.ErrorCode);
        Assert.Equal(409, crossOpConflict.StatusCode);

        var first = await handler.Handle(
            new CancelOrderEscrowCommand("Order expired")
            {
                OrderId = orderId,
                IdempotencyKey = "cancel-key-1"
            },
            default);
        var replay = await handler.Handle(
            new CancelOrderEscrowCommand("Order expired")
            {
                OrderId = orderId,
                IdempotencyKey = "cancel-key-1"
            },
            default);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.TransactionId, replay.TransactionId);
        Assert.Equal(500_000m, buyer.Balance);
        Assert.Single(context.AuditLogs.Where(item => item.Action == AuditAction.OrderEscrowCancel));

        var payloadConflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Different reason")
            {
                OrderId = orderId,
                IdempotencyKey = "cancel-key-1"
            },
            default));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", payloadConflict.ErrorCode);
        Assert.Equal(409, payloadConflict.StatusCode);

        var newKeyAfterCancel = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Order expired")
            {
                OrderId = orderId,
                IdempotencyKey = "cancel-key-2"
            },
            default));
        Assert.Equal("ORDER_ESCROW_ALREADY_CANCELLED", newKeyAfterCancel.ErrorCode);
        Assert.Equal(409, newKeyAfterCancel.StatusCode);
    }

    [Fact]
    public async Task CancelOrderEscrow_StrictAllOrNothing_RejectsPartiallyAndFullyResolvedEscrows()
    {
        using var context = new TestWalletDbContext(nameof(CancelOrderEscrow_StrictAllOrNothing_RejectsPartiallyAndFullyResolvedEscrows));
        var buyer = AddWallet(context, balance: 1_000_000m);
        var seller = AddWallet(context, balance: 0m);
        var handler = new CancelOrderEscrowCommandHandler(context);

        var partialSettledOrder = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = partialSettledOrder, UserId = buyer.UserId.ToString() },
            default);
        await new SettleSellerOrderCommandHandler(context).Handle(
            new SettleSellerOrderCommand(seller.UserId.ToString(), 40_000m) { SubOrderId = partialSettledOrder },
            default);
        var partialSettleErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Attempt cancel") { OrderId = partialSettledOrder, IdempotencyKey = "ps-1" },
            default));
        Assert.Equal("ORDER_ESCROW_PARTIALLY_RESOLVED", partialSettleErr.ErrorCode);
        Assert.Equal(409, partialSettleErr.StatusCode);

        var partialRefundedOrder = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = partialRefundedOrder, UserId = buyer.UserId.ToString() },
            default);
        await new RefundOrderPaymentCommandHandler(context).Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 30_000m, "Partial refund") { OrderId = partialRefundedOrder },
            default);
        var partialRefundErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Attempt cancel") { OrderId = partialRefundedOrder, IdempotencyKey = "pr-1" },
            default));
        Assert.Equal("ORDER_ESCROW_PARTIALLY_RESOLVED", partialRefundErr.ErrorCode);
        Assert.Equal(409, partialRefundErr.StatusCode);

        var settledOrder = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = settledOrder, UserId = buyer.UserId.ToString() },
            default);
        await new SettleSellerOrderCommandHandler(context).Handle(
            new SettleSellerOrderCommand(seller.UserId.ToString(), 100_000m) { SubOrderId = settledOrder },
            default);
        Assert.Equal(OrderEscrowStatus.Settled, context.OrderEscrows.Single(item => item.OrderId == settledOrder).Status);
        var settledErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Attempt cancel") { OrderId = settledOrder, IdempotencyKey = "fs-1" },
            default));
        Assert.Equal("ORDER_ESCROW_ALREADY_RESOLVED", settledErr.ErrorCode);
        Assert.Equal(409, settledErr.StatusCode);

        var refundedOrder = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = refundedOrder, UserId = buyer.UserId.ToString() },
            default);
        await new RefundOrderPaymentCommandHandler(context).Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 100_000m, "Full refund") { OrderId = refundedOrder },
            default);
        Assert.Equal(OrderEscrowStatus.Refunded, context.OrderEscrows.Single(item => item.OrderId == refundedOrder).Status);
        var refundedErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Attempt cancel") { OrderId = refundedOrder, IdempotencyKey = "fr-1" },
            default));
        Assert.Equal("ORDER_ESCROW_ALREADY_RESOLVED", refundedErr.ErrorCode);
        Assert.Equal(409, refundedErr.StatusCode);
    }

    [Fact]
    public async Task AfterCancelOrderEscrow_SubsequentSettleRefundAndAdjustFailWith409Cancelled()
    {
        using var context = new TestWalletDbContext(nameof(AfterCancelOrderEscrow_SubsequentSettleRefundAndAdjustFailWith409Cancelled));
        var buyer = AddWallet(context, balance: 500_000m);
        var seller = AddWallet(context, balance: 0m);
        var orderId = Guid.NewGuid();

        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = orderId, UserId = buyer.UserId.ToString() },
            default);
        await new CancelOrderEscrowCommandHandler(context).Handle(
            new CancelOrderEscrowCommand("Order cancelled") { OrderId = orderId, IdempotencyKey = "cancel-seq-1" },
            default);

        var settleErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new SettleSellerOrderCommandHandler(context).Handle(
                new SettleSellerOrderCommand(seller.UserId.ToString(), 50_000m) { SubOrderId = orderId },
                default));
        Assert.Equal("ORDER_ESCROW_CANCELLED", settleErr.ErrorCode);
        Assert.Equal(409, settleErr.StatusCode);

        var refundErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new RefundOrderPaymentCommandHandler(context).Handle(
                new RefundOrderPaymentCommand(buyer.UserId.ToString(), 50_000m, "Duplicate refund") { OrderId = orderId },
                default));
        Assert.Equal("ORDER_ESCROW_CANCELLED", refundErr.ErrorCode);
        Assert.Equal(409, refundErr.StatusCode);

        var adjustPlusErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new AdjustOrderPaymentCommandHandler(context).Handle(
                new AdjustOrderPaymentCommand(buyer.UserId.ToString(), 10_000m, "Delta plus")
                {
                    OrderId = orderId,
                    IdempotencyKey = "adj-after-cancel-1"
                },
                default));
        Assert.Equal("ORDER_ESCROW_CANCELLED", adjustPlusErr.ErrorCode);
        Assert.Equal(409, adjustPlusErr.StatusCode);

        var adjustMinusErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new AdjustOrderPaymentCommandHandler(context).Handle(
                new AdjustOrderPaymentCommand(buyer.UserId.ToString(), -10_000m, "Delta minus")
                {
                    OrderId = orderId,
                    IdempotencyKey = "adj-after-cancel-2"
                },
                default));
        Assert.Equal("ORDER_ESCROW_CANCELLED", adjustMinusErr.ErrorCode);
        Assert.Equal(409, adjustMinusErr.StatusCode);
    }

    [Fact]
    public async Task CancelOrderEscrow_OptionalGuardsAndMissingOrAmbiguousEscrowErrors()
    {
        using var context = new TestWalletDbContext(nameof(CancelOrderEscrow_OptionalGuardsAndMissingOrAmbiguousEscrowErrors));
        var buyer = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();
        var handler = new CancelOrderEscrowCommandHandler(context);

        var notFound = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Missing order") { OrderId = Guid.NewGuid(), IdempotencyKey = "missing-1" },
            default));
        Assert.Equal("ORDER_ESCROW_NOT_FOUND", notFound.ErrorCode);
        Assert.Equal(404, notFound.StatusCode);

        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = orderId, UserId = buyer.UserId.ToString() },
            default);

        var wrongBuyer = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Wrong buyer", Guid.NewGuid().ToString(), 100_000m)
            {
                OrderId = orderId,
                IdempotencyKey = "wrong-buyer-1"
            },
            default));
        Assert.Equal("ORDER_ESCROW_OWNERSHIP_CONFLICT", wrongBuyer.ErrorCode);
        Assert.Equal(409, wrongBuyer.StatusCode);

        var wrongAmount = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Wrong amount", buyer.UserId.ToString(), 99_000m)
            {
                OrderId = orderId,
                IdempotencyKey = "wrong-amount-1"
            },
            default));
        Assert.Equal("ORDER_ESCROW_AMOUNT_MISMATCH", wrongAmount.ErrorCode);
        Assert.Equal(409, wrongAmount.StatusCode);

        var matched = await handler.Handle(
            new CancelOrderEscrowCommand("Matched guards", buyer.UserId.ToString(), 100_000m)
            {
                OrderId = orderId,
                IdempotencyKey = "matched-1"
            },
            default);
        Assert.Equal(100_000m, matched.ReturnedAmount);

        var ambiguousOrderId = Guid.NewGuid();
        context.WalletTransactions.AddRange(
            new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = buyer.Id,
                UserId = buyer.UserId.ToString(),
                Type = WalletTransactionType.PurchaseDebit,
                Amount = 50_000m,
                Currency = "Vnd",
                BalanceBefore = 500_000m,
                BalanceAfter = 450_000m,
                Status = TransactionStatus.Success,
                ReferenceId = ambiguousOrderId
            },
            new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = buyer.Id,
                UserId = buyer.UserId.ToString(),
                Type = WalletTransactionType.PurchaseDebit,
                Amount = 50_000m,
                Currency = "Vnd",
                BalanceBefore = 450_000m,
                BalanceAfter = 400_000m,
                Status = TransactionStatus.Success,
                ReferenceId = ambiguousOrderId
            });
        await context.SaveChangesAsync();

        var ambiguousErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CancelOrderEscrowCommand("Ambiguous payments") { OrderId = ambiguousOrderId, IdempotencyKey = "ambig-1" },
            default));
        Assert.Equal("ORDER_ESCROW_UNAVAILABLE", ambiguousErr.ErrorCode);
        Assert.Equal(409, ambiguousErr.StatusCode);
    }

    [Fact]
    public void CancelOrderEscrowCommand_ValidationRules()
    {
        var valid = new CancelOrderEscrowCommand("Order cancelled")
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "cancel-valid-1"
        };
        Assert.True(RequestValidation.TryValidate(valid, new List<ValidationResult>()));

        var validWithOptional = new CancelOrderEscrowCommand("Order cancelled", Guid.NewGuid().ToString(), 50_000.25m)
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "cancel-valid-2"
        };
        Assert.True(RequestValidation.TryValidate(validWithOptional, new List<ValidationResult>()));

        var emptyOrderId = new CancelOrderEscrowCommand("Order cancelled")
        {
            OrderId = Guid.Empty,
            IdempotencyKey = "cancel-valid-1"
        };
        var emptyOrderResults = new List<ValidationResult>();
        Assert.False(RequestValidation.TryValidate(emptyOrderId, emptyOrderResults));

        var invalidKey = new CancelOrderEscrowCommand("Order cancelled")
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "bad key!"
        };
        Assert.False(RequestValidation.TryValidate(invalidKey, new List<ValidationResult>()));

        var shortReason = new CancelOrderEscrowCommand("ab")
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "cancel-valid-1"
        };
        Assert.False(RequestValidation.TryValidate(shortReason, new List<ValidationResult>()));

        var blankReason = new CancelOrderEscrowCommand("   ")
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "cancel-valid-1"
        };
        Assert.False(RequestValidation.TryValidate(blankReason, new List<ValidationResult>()));

        var invalidBuyer = new CancelOrderEscrowCommand("Order cancelled", "not-a-guid")
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "cancel-valid-1"
        };
        Assert.False(RequestValidation.TryValidate(invalidBuyer, new List<ValidationResult>()));

        foreach (var badAmount in new[] { 0m, -10m, 100.123m })
        {
            var invalidAmount = new CancelOrderEscrowCommand("Order cancelled", ExpectedAmount: badAmount)
            {
                OrderId = Guid.NewGuid(),
                IdempotencyKey = "cancel-valid-1"
            };
            Assert.False(RequestValidation.TryValidate(invalidAmount, new List<ValidationResult>()));
        }
    }

    [Fact]
    public async Task ListTransactions_RefundFilter_IncludesEscrowCancellationRefundAsPascalCase()
    {
        using var context = new TestWalletDbContext(nameof(ListTransactions_RefundFilter_IncludesEscrowCancellationRefundAsPascalCase));
        var buyer = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();

        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = orderId, UserId = buyer.UserId.ToString() },
            default);
        await new CancelOrderEscrowCommandHandler(context).Handle(
            new CancelOrderEscrowCommand("Cancelled order") { OrderId = orderId, IdempotencyKey = "cancel-list-1" },
            default);

        var page = await new ListTransactionsQueryHandler(context).Handle(
            new ListTransactionsQuery(Type: "REFUND") { UserId = buyer.UserId.ToString() },
            default);

        var item = Assert.Single(page.Items);
        Assert.Equal("EscrowCancellationRefund", item.Type);
        Assert.Equal(100_000m, item.Amount);
    }

    [Fact]
    public async Task CancelOrderEscrowInternal_ControllerActionAttributesAndDispatch()
    {
        var method = typeof(WalletsController).GetMethod(nameof(WalletsController.CancelOrderEscrowInternal))!;
        Assert.NotNull(method.GetCustomAttribute<RequireInternalServiceKeyAttribute>());
        Assert.Null(method.GetCustomAttribute<ReleaseFeatureAttribute>());
        Assert.True(method.GetCustomAttribute<ApiExplorerSettingsAttribute>()?.IgnoreApi);
        var postAttr = method.GetCustomAttribute<HttpPostAttribute>();
        Assert.Equal("~/api/v1/internal/wallets/orders/{orderId:guid}/escrow/cancel", postAttr?.Template);

        CancelOrderEscrowCommand? captured = null;
        var sender = new CapturingSender(cmd => captured = cmd);
        var controller = new WalletsController(sender, NullLogger<WalletsController>.Instance);
        var orderId = Guid.NewGuid();

        var actionResult = await controller.CancelOrderEscrowInternal(
            orderId,
            new CancelOrderEscrowCommand("Order cancelled"),
            "  cancel-hdr-1  ",
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        Assert.NotNull(captured);
        Assert.Equal(orderId, captured!.OrderId);
        Assert.Equal("cancel-hdr-1", captured.IdempotencyKey);
        Assert.IsType<CancelOrderEscrowResponse>(ok.Value);
    }

    private static WalletAccount AddWallet(TestWalletDbContext context, decimal balance)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Balance = balance,
            Currency = CurrencyCode.Vnd
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private sealed class CapturingSender(Action<CancelOrderEscrowCommand> onSend) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var cmd = Assert.IsType<CancelOrderEscrowCommand>(request);
            onSend(cmd);
            object response = new CancelOrderEscrowResponse(
                Guid.NewGuid().ToString(),
                cmd.OrderId.ToString(),
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "EscrowCancellationRefund",
                100_000m,
                "Vnd",
                400_000m,
                500_000m,
                "Cancelled",
                "Success",
                $"Order escrow cancelled: {cmd.Reason}",
                DateTimeOffset.UtcNow,
                false);
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
