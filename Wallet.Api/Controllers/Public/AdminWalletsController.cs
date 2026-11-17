namespace Wallet.Api.Controllers;

using System;
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
using Wallet.Application.Features.AdminWallets.Queries.GetWalletReconciliation;
using Wallet.Application.Features.AdminWallets.Queries.ListWalletLedger;

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

    /// <summary>Approves or rejects a withdrawal request.</summary>
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
            command with { WithdrawalId = id, AdminUserId = adminId },
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
        [FromQuery] ListWalletLedgerQuery query,
        CancellationToken cancellationToken)
    {
        if (!this.EnsureAdmin(out var authError))
        {
            return authError;
        }

        var result = await _sender.Send(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>Compares wallet balances with their latest successful transaction snapshots. Admin role required.</summary>
    [HttpGet("reconciliation")]
    [ProducesResponseType(typeof(PagedData<WalletReconciliationResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ReconcileWallets(
        [FromQuery] GetWalletReconciliationQuery query,
        CancellationToken cancellationToken)
    {
        if (!this.EnsureAdmin(out var authError))
        {
            return authError;
        }

        var result = await _sender.Send(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>Manually adjusts a wallet balance by administrative action.</summary>
    [HttpPost("{userId:guid}/adjustments")]
    public async Task<IActionResult> AdjustBalance(
        Guid userId,
        [FromBody] AdjustWalletBalanceCommand command,
        CancellationToken cancellationToken)
    {
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
            command with { UserId = userId, AdminUserId = adminId },
            cancellationToken);
        return Ok(result);
    }
}
