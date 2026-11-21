namespace Wallet.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Wallet.Api.Extensions;
using Wallet.Api.Filters;
using Wallet.Application.Common.Models;
using Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Features.AdminWallets.Commands.ResolveStripeAttempt;
using Wallet.Application.Features.AdminWallets.Queries.GetWalletReconciliation;
using Wallet.Application.Features.AdminWallets.Queries.GetWithdrawalHistory;
using Wallet.Application.Features.AdminWallets.Queries.ListAdminHolds;
using Wallet.Application.Features.AdminWallets.Queries.ListAdminWallets;
using Wallet.Application.Features.AdminWallets.Queries.ListAdminWithdrawals;
using Wallet.Application.Features.AdminWallets.Queries.ListStripeReconciliationRequired;
using Wallet.Application.Features.AdminWallets.Queries.ListWalletLedger;
using Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;
using Wallet.Application.Features.Wallets;
using Wallet.Application.Features.Wallets.Commands.FreezeWallet;
using Wallet.Application.Features.Wallets.Commands.UnfreezeWallet;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;
using Wallet.Application.Features.Withdrawals;

[ApiController]
[Route("api/v1/admin/wallets")]
[RequireRole("admin", "staff")]
public sealed class AdminWalletsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<AdminWalletsController> _logger;

    public AdminWalletsController(ISender sender, ILogger<AdminWalletsController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>Lists paginated wallets for admin and staff review.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedData<WalletResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWallets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? search = null,
        [FromQuery] string? currency = null,
        CancellationToken cancellationToken = default) =>
        Ok(await _sender.Send(
            new ListAdminWalletsQuery(page, pageSize, status, userId, search, currency),
            cancellationToken));

    /// <summary>Lists paginated withdrawal requests for admin and staff review.</summary>
    [HttpGet("withdrawals")]
    [ProducesResponseType(typeof(PagedData<AdminWithdrawalResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWithdrawals(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? transferReference = null,
        [FromQuery] DateTimeOffset? fromDate = null,
        [FromQuery] DateTimeOffset? toDate = null,
        CancellationToken cancellationToken = default) =>
        Ok(await _sender.Send(
            new ListAdminWithdrawalsQuery(page, pageSize, status, userId, transferReference, fromDate, toDate),
            cancellationToken));

    /// <summary>Gets the audit history of a withdrawal request.</summary>
    [HttpGet("withdrawals/{id:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<WithdrawalHistoryEntry>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWithdrawalHistory(Guid id, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetWithdrawalHistoryQuery(id), cancellationToken));

    /// <summary>Processes a withdrawal workflow transition.</summary>
    [HttpPatch("withdrawals/{id:guid}")]
    public async Task<IActionResult> ProcessWithdrawal(
        Guid id,
        [FromBody] ProcessWithdrawalCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var adminUserId, out var authError))
        {
            return authError;
        }

        var adminId = adminUserId.ToString();
        _logger.LogInformation(
            "Processing withdrawal '{WithdrawalId}' by admin '{AdminId}' with action '{Action}'",
            id,
            adminId,
            command.Action);
        var result = await _sender.Send(
            command with { WithdrawalId = id, AdminUserId = adminId, ActorIsAdmin = this.IsAdmin() },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Returns a paginated transaction report. Admin role required; staff callers are forbidden.</summary>
    [HttpGet("ledger")]
    [ProducesResponseType(typeof(PagedData<AdminWalletLedgerEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListLedger(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] Guid? walletId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTimeOffset? fromDate = null,
        [FromQuery] DateTimeOffset? toDate = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] Guid? referenceId = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.EnsureAdmin(out var authError))
        {
            return authError;
        }

        var result = await _sender.Send(
            new ListWalletLedgerQuery(page, pageSize, walletId, type, status, fromDate, toDate, userId, referenceId),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Compares wallet balances with their latest successful transaction snapshots. Admin role required.</summary>
    [HttpGet("reconciliation")]
    [ProducesResponseType(typeof(PagedData<WalletReconciliationResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ReconcileWallets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] Guid? walletId = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? result = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.EnsureAdmin(out var authError))
        {
            return authError;
        }

        var response = await _sender.Send(
            new GetWalletReconciliationQuery(page, pageSize, walletId, userId, result),
            cancellationToken);
        return Ok(response);
    }

    /// <summary>Manually adjusts a wallet balance by administrative action. Admin role required.</summary>
    [HttpPost("{userId:guid}/adjustments")]
    public async Task<IActionResult> AdjustBalance(
        Guid userId,
        [FromBody] AdjustWalletBalanceCommand command,
        CancellationToken cancellationToken = default,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        if (!this.EnsureAdmin(out var adminError))
        {
            return adminError;
        }

        if (!this.TryGetUserId(out var adminUserId, out var authError))
        {
            return authError;
        }

        var adminId = adminUserId.ToString();
        _logger.LogInformation(
            "Adjusting wallet for user '{UserId}' by admin '{AdminId}', Amount: {Amount}",
            userId,
            adminId,
            command.Amount);
        var result = await _sender.Send(
            command with
            {
                UserId = userId,
                AdminUserId = adminId,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
            },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Gets a user's wallet details for admin and staff inspection.</summary>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserWallet(Guid userId, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetMyWalletQuery(userId.ToString()) { AutoCreate = false }, cancellationToken));

    /// <summary>Freezes a user's wallet.</summary>
    [HttpPost("{userId:guid}/freeze")]
    [ProducesResponseType(typeof(WalletFreezeStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> FreezeWallet(
        Guid userId,
        [FromBody] AdminWalletFreezeRequest? request,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var adminUserId, out var authError))
        {
            return authError;
        }

        _logger.LogInformation(
            "Freezing wallet for user '{UserId}' by '{AdminId}' with reason '{Reason}'",
            userId,
            adminUserId,
            request?.Reason);
        return Ok(await _sender.Send(
            new FreezeWalletCommand(userId, request?.Reason) { ActorUserId = adminUserId.ToString() },
            cancellationToken));
    }

    /// <summary>Unfreezes a user's wallet.</summary>
    [HttpPost("{userId:guid}/unfreeze")]
    [ProducesResponseType(typeof(WalletFreezeStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnfreezeWallet(
        Guid userId,
        [FromBody] AdminWalletFreezeRequest? request,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var adminUserId, out var authError))
        {
            return authError;
        }

        _logger.LogInformation(
            "Unfreezing wallet for user '{UserId}' by '{AdminId}' with reason '{Reason}'",
            userId,
            adminUserId,
            request?.Reason);
        return Ok(await _sender.Send(
            new UnfreezeWalletCommand(userId, request?.Reason) { ActorUserId = adminUserId.ToString() },
            cancellationToken));
    }

    /// <summary>Lists auction deposit holds across wallets for admin and staff support.</summary>
    [HttpGet("holds")]
    [ProducesResponseType(typeof(PagedData<AdminHoldListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListHolds(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await _sender.Send(new ListAdminHoldsQuery(page, pageSize, userId, status), cancellationToken));

    /// <summary>Lists auction deposit holds for a specific user wallet.</summary>
    [HttpGet("{userId:guid}/holds")]
    [ProducesResponseType(typeof(PagedData<AdminHoldListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUserHolds(
        Guid userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await _sender.Send(new ListAdminHoldsQuery(page, pageSize, userId, status), cancellationToken));

    /// <summary>Lists Stripe payment attempts requiring manual reconciliation. Admin role required.</summary>
    [HttpGet("stripe/reconciliation-required")]
    [ProducesResponseType(typeof(PagedData<StripeReconciliationAttemptItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListStripeReconciliationRequired(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? purpose = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.EnsureAdmin(out var adminError))
        {
            return adminError;
        }

        return Ok(await _sender.Send(
            new ListStripeReconciliationRequiredQuery(page, pageSize, purpose),
            cancellationToken));
    }

    /// <summary>Manually resolves a Stripe payment attempt stuck in ReconciliationRequired. Admin role required.</summary>
    [HttpPost("stripe/attempts/{attemptId:guid}/resolve")]
    [ProducesResponseType(typeof(ResolveStripeAttemptResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveStripeAttempt(
        Guid attemptId,
        [FromBody] ResolveStripeAttemptCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.EnsureAdmin(out var adminError))
        {
            return adminError;
        }

        if (!this.TryGetUserId(out var adminUserId, out var authError))
        {
            return authError;
        }

        return Ok(await _sender.Send(
            command with { AttemptId = attemptId, AdminUserId = adminUserId.ToString() },
            cancellationToken));
    }

    /// <summary>Triggers manual reconciliation of pending Stripe topups and direct payments. Admin role required.</summary>
    [HttpPost("stripe/reconcile-pending")]
    public async Task<IActionResult> ReconcilePendingStripe(CancellationToken cancellationToken)
    {
        if (!this.EnsureAdmin(out var adminError))
        {
            return adminError;
        }

        var processedCount = await _sender.Send(new ReconcilePendingTopupsCommand(), cancellationToken);
        return Ok(new { processedCount });
    }
}

public sealed record AdminWalletFreezeRequest(string? Reason);
