namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Features.AdminWallets.Queries.GetWithdrawalHistory;
using Wallet.Application.Features.AdminWallets.Queries.ListAdminWithdrawals;
using Wallet.Application.Features.Withdrawals.Commands.CancelWithdrawal;
using Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;
using Wallet.Application.Features.Withdrawals.Queries.GetMyWithdrawal;
using Wallet.Application.Features.Withdrawals.Queries.ListMyWithdrawals;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class WithdrawalWorkflowTests
{
    [Fact]
    public async Task RequestWithdrawal_IdempotentReplay_ReturnsSameWithdrawalWithoutDoubleLock()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var userId = Guid.NewGuid();
        var wallet = AddWallet(context, userId, 500_000m);
        var handler = new RequestWithdrawalCommandHandler(context);
        var command = new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller Name")
        {
            UserId = userId.ToString(),
            IdempotencyKey = "wd-idem-1"
        };

        var first = await handler.Handle(command, default);
        var second = await handler.Handle(command, default);

        Assert.False(first.IsReplay);
        Assert.True(second.IsReplay);
        Assert.Equal(first.WithdrawalId, second.WithdrawalId);
        Assert.Equal(100_000m, wallet.LockedBalance);
        Assert.Single(context.WithdrawalRequests);
    }

    [Fact]
    public async Task RequestWithdrawal_IdempotencyKeyReusedWithDifferentAmount_Returns409()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var userId = Guid.NewGuid();
        AddWallet(context, userId, 500_000m);
        var handler = new RequestWithdrawalCommandHandler(context);

        await handler.Handle(new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller Name")
        {
            UserId = userId.ToString(),
            IdempotencyKey = "wd-idem-conflict"
        }, default);

        var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            handler.Handle(new RequestWithdrawalCommand(150_000m, "VND", "VCB", "1234567890", "Seller Name")
            {
                UserId = userId.ToString(),
                IdempotencyKey = "wd-idem-conflict"
            }, default));

        Assert.Equal("IDEMPOTENCY_KEY_REUSED", error.ErrorCode);
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task ProcessWithdrawal_FullHappyPath_ApproveStartProcessingComplete()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var sellerId = Guid.NewGuid();
        var wallet = AddWallet(context, sellerId, 500_000m);
        var created = await new RequestWithdrawalCommandHandler(context).Handle(
            new RequestWithdrawalCommand(120_000m, "VND", "ACB", "9988776655", "Alice Seller")
            {
                UserId = sellerId.ToString(),
                IdempotencyKey = "wd-happy-1"
            },
            default);

        var withdrawalId = Guid.Parse(created.WithdrawalId);
        var adminId = Guid.NewGuid().ToString();
        var processHandler = new ProcessWithdrawalCommandHandler(context);

        var approved = await processHandler.Handle(
            new ProcessWithdrawalCommand("Approve") { WithdrawalId = withdrawalId, AdminUserId = adminId },
            default);
        Assert.Equal("Approved", approved.Status);
        Assert.Equal(380_000m, wallet.Balance);
        Assert.Equal(0m, wallet.LockedBalance);

        var processing = await processHandler.Handle(
            new ProcessWithdrawalCommand("StartProcessing") { WithdrawalId = withdrawalId, AdminUserId = adminId },
            default);
        Assert.Equal("Processing", processing.Status);

        var completed = await processHandler.Handle(
            new ProcessWithdrawalCommand("Complete", TransferConfirmed: true, TransferReference: "FT-2026-999")
            {
                WithdrawalId = withdrawalId,
                AdminUserId = adminId
            },
            default);
        Assert.Equal("Completed", completed.Status);
        Assert.Equal("FT-2026-999", completed.TransferReference);

        var entity = context.WithdrawalRequests.Single(r => r.Id == withdrawalId);
        Assert.NotNull(entity.CompletedAt);

        var history = await new GetWithdrawalHistoryQueryHandler(context)
            .Handle(new GetWithdrawalHistoryQuery(withdrawalId), default);
        Assert.Equal(4, history.Count);
        Assert.Equal(
            new[]
            {
                nameof(AuditAction.WithdrawalRequest),
                nameof(AuditAction.WithdrawalApprove),
                nameof(AuditAction.WithdrawalStartProcessing),
                nameof(AuditAction.WithdrawalComplete)
            },
            history.Select(h => h.Action).ToArray());
    }

    [Fact]
    public async Task ProcessWithdrawal_DuplicateTransferReference_Returns409()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var sellerId = Guid.NewGuid();
        AddWallet(context, sellerId, 500_000m);
        var reqHandler = new RequestWithdrawalCommandHandler(context);
        var w1 = Guid.Parse((await reqHandler.Handle(
            new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1111222233", "Seller")
            { UserId = sellerId.ToString(), IdempotencyKey = "dup-1" }, default)).WithdrawalId);
        var w2 = Guid.Parse((await reqHandler.Handle(
            new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1111222233", "Seller")
            { UserId = sellerId.ToString(), IdempotencyKey = "dup-2" }, default)).WithdrawalId);

        var adminId = Guid.NewGuid().ToString();
        var proc = new ProcessWithdrawalCommandHandler(context);
        await proc.Handle(new ProcessWithdrawalCommand("Approve") { WithdrawalId = w1, AdminUserId = adminId }, default);
        await proc.Handle(new ProcessWithdrawalCommand("Complete", TransferConfirmed: true, TransferReference: "REF-DUP")
        { WithdrawalId = w1, AdminUserId = adminId }, default);

        await proc.Handle(new ProcessWithdrawalCommand("Approve") { WithdrawalId = w2, AdminUserId = adminId }, default);
        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            proc.Handle(new ProcessWithdrawalCommand("Complete", TransferConfirmed: true, TransferReference: "REF-DUP")
            { WithdrawalId = w2, AdminUserId = adminId }, default));

        Assert.Equal("TRANSFER_REFERENCE_ALREADY_USED", conflict.ErrorCode);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task ProcessWithdrawal_FailFromApproved_ReversesDebitAndExposesSafeReason()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var sellerId = Guid.NewGuid();
        var wallet = AddWallet(context, sellerId, 500_000m);
        var created = await new RequestWithdrawalCommandHandler(context).Handle(
            new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller")
            { UserId = sellerId.ToString(), IdempotencyKey = "fail-1" }, default);

        var id = Guid.Parse(created.WithdrawalId);
        var adminId = Guid.NewGuid().ToString();
        var proc = new ProcessWithdrawalCommandHandler(context);
        await proc.Handle(new ProcessWithdrawalCommand("Approve") { WithdrawalId = id, AdminUserId = adminId }, default);

        var failed = await proc.Handle(
            new ProcessWithdrawalCommand("Fail", AdminNote: "Invalid beneficiary bank account")
            { WithdrawalId = id, AdminUserId = adminId }, default);

        Assert.Equal("Failed", failed.Status);
        Assert.Equal("Invalid beneficiary bank account", failed.StatusReason);
        Assert.Equal(500_000m, wallet.Balance);
        Assert.Single(context.WalletTransactions.Where(t => t.Type == WalletTransactionType.WithdrawalReversal));
    }

    [Fact]
    public async Task ProcessWithdrawal_ResolveFailed_RequiresAdminAndHidesVerificationEvidenceFromSeller()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var sellerId = Guid.NewGuid();
        var wallet = AddWallet(context, sellerId, 500_000m);
        var created = await new RequestWithdrawalCommandHandler(context).Handle(
            new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller")
            { UserId = sellerId.ToString(), IdempotencyKey = "resolve-1" }, default);

        var id = Guid.Parse(created.WithdrawalId);
        var staffId = Guid.NewGuid().ToString();
        var adminId = Guid.NewGuid().ToString();
        var proc = new ProcessWithdrawalCommandHandler(context);

        await proc.Handle(new ProcessWithdrawalCommand("Approve") { WithdrawalId = id, AdminUserId = staffId }, default);
        await proc.Handle(new ProcessWithdrawalCommand("StartProcessing") { WithdrawalId = id, AdminUserId = staffId }, default);

        var resolveCommand = new ProcessWithdrawalCommand(
            "ResolveFailed",
            AdminNote: "Bank rejected transfer",
            BankVerifiedNotTransferred: true,
            VerificationEvidence: "Internal bank statement #INTERNAL-998")
        {
            WithdrawalId = id,
            AdminUserId = staffId,
            ActorIsAdmin = false
        };

        var forbidden = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            proc.Handle(resolveCommand, default));
        Assert.Equal("FORBIDDEN", forbidden.ErrorCode);
        Assert.Equal(403, forbidden.StatusCode);

        var resolved = await proc.Handle(
            resolveCommand with { AdminUserId = adminId, ActorIsAdmin = true },
            default);

        Assert.Equal("Failed", resolved.Status);
        Assert.Equal("Bank rejected transfer", resolved.StatusReason);
        Assert.Equal(500_000m, wallet.Balance);

        var sellerView = await new GetMyWithdrawalQueryHandler(context)
            .Handle(new GetMyWithdrawalQuery(id) { UserId = sellerId }, default);
        Assert.Equal("******7890", sellerView.MaskedAccountNumber);
        Assert.Equal("Bank rejected transfer", sellerView.StatusReason);
        Assert.DoesNotContain("INTERNAL-998", sellerView.StatusReason ?? string.Empty);

        var adminList = await new ListAdminWithdrawalsQueryHandler(context)
            .Handle(new ListAdminWithdrawalsQuery(1, 20, "Failed"), default);
        var adminItem = Assert.Single(adminList.Items);
        Assert.Equal("1234567890", adminItem.BankAccountNumber);

        var myList = await new ListMyWithdrawalsQueryHandler(context)
            .Handle(new ListMyWithdrawalsQuery(1, 20) { UserId = sellerId }, default);
        Assert.Single(myList.Items);
    }

    [Fact]
    public async Task CancelWithdrawal_PendingReleasesLock_ApprovedReturns409_NonOwnerReturns404()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var sellerId = Guid.NewGuid();
        var wallet = AddWallet(context, sellerId, 500_000m);
        var reqHandler = new RequestWithdrawalCommandHandler(context);
        var cancelHandler = new CancelWithdrawalCommandHandler(context);

        var pendingResp = await reqHandler.Handle(
            new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller")
            { UserId = sellerId.ToString(), IdempotencyKey = "cancel-1" }, default);
        var pendingId = Guid.Parse(pendingResp.WithdrawalId);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            cancelHandler.Handle(new CancelWithdrawalCommand(pendingId) { UserId = Guid.NewGuid() }, default));

        var cancelled = await cancelHandler.Handle(new CancelWithdrawalCommand(pendingId) { UserId = sellerId }, default);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(0m, wallet.LockedBalance);
        Assert.Equal(500_000m, wallet.AvailableBalance);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.WithdrawalCancel));

        var secondResp = await reqHandler.Handle(
            new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller")
            { UserId = sellerId.ToString(), IdempotencyKey = "cancel-2" }, default);
        var secondId = Guid.Parse(secondResp.WithdrawalId);
        await new ProcessWithdrawalCommandHandler(context).Handle(
            new ProcessWithdrawalCommand("Approve") { WithdrawalId = secondId, AdminUserId = Guid.NewGuid().ToString() },
            default);

        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            cancelHandler.Handle(new CancelWithdrawalCommand(secondId) { UserId = sellerId }, default));
        Assert.Equal("WITHDRAWAL_INVALID_TRANSITION", conflict.ErrorCode);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task WithdrawalHistory_RedactsBankDetailsAndEvidence_ButKeepsWorkflowFields()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var sellerId = Guid.NewGuid();
        AddWallet(context, sellerId, 500_000m);
        var created = await new RequestWithdrawalCommandHandler(context).Handle(
            new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller")
            { UserId = sellerId.ToString(), IdempotencyKey = "history-redact" }, default);
        var id = Guid.Parse(created.WithdrawalId);

        context.AddAuditLog(
            AuditAction.WithdrawalApprove,
            nameof(WithdrawalRequest),
            id.ToString(),
            oldValues: "{\"Status\":\"Pending\",\"BankAccountNumber\":\"1234567890\"}",
            newValues: "{\"Status\":\"Approved\",\"Reason\":\"ok\",\"TransferReference\":\"REF-1\"," +
                       "\"bankname\":\"VCB\",\"AccountNumber\":\"1\",\"BankAccountHolder\":\"S\"," +
                       "\"BankCode\":\"VCB\",\"AccountHolderName\":\"S\",\"BankAccountId\":\"x\"," +
                       "\"VerificationEvidence\":\"SECRET\"}");
        context.AddAuditLog(AuditAction.WithdrawalFail, nameof(WithdrawalRequest), id.ToString(), newValues: "not-json");
        await context.SaveChangesAsync();

        var history = await new GetWithdrawalHistoryQueryHandler(context)
            .Handle(new GetWithdrawalHistoryQuery(id), default);

        var requestEntry = history.Single(h => h.Action == nameof(AuditAction.WithdrawalRequest));
        Assert.DoesNotContain("BankName", requestEntry.NewValues);
        Assert.Contains("LockedBalance", requestEntry.NewValues);

        var approve = history.Single(h => h.Action == nameof(AuditAction.WithdrawalApprove));
        Assert.DoesNotContain("BankAccountNumber", approve.OldValues);
        Assert.Contains("Pending", approve.OldValues);
        foreach (var redacted in new[]
                 {
                     "bankname", "AccountNumber", "BankAccountHolder", "BankCode",
                     "AccountHolderName", "BankAccountId", "VerificationEvidence", "SECRET"
                 })
        {
            Assert.DoesNotContain(redacted, approve.NewValues);
        }

        Assert.Contains("TransferReference", approve.NewValues);
        Assert.Contains("REF-1", approve.NewValues);
        Assert.Contains("Reason", approve.NewValues);

        var invalidJson = history.Single(h => h.Action == nameof(AuditAction.WithdrawalFail));
        Assert.Equal("not-json", invalidJson.NewValues);
    }

    [Fact]
    public async Task ProcessWithdrawal_LegacyReferenceCode_WorksOnApproveAndComplete_AndEnforcesUniqueness()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var sellerId = Guid.NewGuid();
        AddWallet(context, sellerId, 500_000m);
        var request = new RequestWithdrawalCommandHandler(context);
        Guid Create(string key) => Guid.Parse(request.Handle(
            new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller")
            { UserId = sellerId.ToString(), IdempotencyKey = key }, default).Result.WithdrawalId);
        var w1 = Create("legacy-1");
        var w2 = Create("legacy-2");
        var w3 = Create("legacy-3");

        var adminId = Guid.NewGuid().ToString();
        var proc = new ProcessWithdrawalCommandHandler(context);

        var approved = await proc.Handle(
            new ProcessWithdrawalCommand("Approve", ReferenceCode: "LEGACY-REF") { WithdrawalId = w1, AdminUserId = adminId },
            default);
        Assert.Equal("LEGACY-REF", approved.TransferReference);
        Assert.Equal("LEGACY-REF", approved.ReferenceCode);

        var duplicateOnApprove = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            proc.Handle(
                new ProcessWithdrawalCommand("Approve", ReferenceCode: "LEGACY-REF") { WithdrawalId = w2, AdminUserId = adminId },
                default));
        Assert.Equal("TRANSFER_REFERENCE_ALREADY_USED", duplicateOnApprove.ErrorCode);
        Assert.Equal(409, duplicateOnApprove.StatusCode);

        await proc.Handle(new ProcessWithdrawalCommand("Approve") { WithdrawalId = w3, AdminUserId = adminId }, default);
        var completed = await proc.Handle(
            new ProcessWithdrawalCommand("Complete", TransferConfirmed: true, ReferenceCode: "LEGACY-DONE")
            { WithdrawalId = w3, AdminUserId = adminId },
            default);
        Assert.Equal("LEGACY-DONE", completed.TransferReference);
        Assert.Equal("LEGACY-DONE", completed.ReferenceCode);

        var matching = new ProcessWithdrawalCommand("Approve", TransferReference: "SAME", ReferenceCode: " SAME ")
        {
            WithdrawalId = w2,
            AdminUserId = adminId
        };
        Assert.False(matching.HasConflictingReferences);

        var conflicting = new ProcessWithdrawalCommand("Approve", TransferReference: "A", ReferenceCode: "B")
        {
            WithdrawalId = w2,
            AdminUserId = adminId
        };
        Assert.True(conflicting.HasConflictingReferences);
        var validation = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        Assert.False(Wallet.Application.Common.Validation.RequestValidation.TryValidate(conflicting, validation));
        var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => proc.Handle(conflicting, default));
        Assert.Equal("VAL_INVALID_INPUT", error.ErrorCode);
        Assert.Equal(WithdrawalStatus.Pending, context.WithdrawalRequests.Single(r => r.Id == w2).Status);
    }

    private static WalletAccount AddWallet(TestWalletDbContext context, Guid userId, decimal balance)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Balance = balance,
            LockedBalance = 0m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }
}
