namespace Wallet.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Wallet.Api.Extensions;
using Wallet.Api.Filters;
using Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;

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

    /// <summary>Manually adjusts a wallet balance by administrative action.</summary>
    [HttpPost("{walletId:guid}/adjustments")]
    public async Task<IActionResult> AdjustBalance(
        Guid walletId,
        [FromBody] AdjustWalletBalanceCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var adminUserId, out var authError))
        {
            return authError;
        }

        var adminId = adminUserId.ToString();
        _logger.LogInformation(
            "Adjusting wallet '{WalletId}' by admin '{AdminId}', Amount: {Amount}",
            walletId,
            adminId,
            command.Amount);
        var result = await _sender.Send(
            command with { WalletId = walletId, AdminUserId = adminId },
            cancellationToken);
        return Ok(result);
    }
}
