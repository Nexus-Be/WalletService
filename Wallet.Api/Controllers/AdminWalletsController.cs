namespace Wallet.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Wallet.Application.Services.Admin;

[ApiController]
[Route("api/v1/admin/wallets")]
public sealed class AdminWalletsController : ControllerBase
{
    private readonly IAdminWalletService _adminWalletService;
    private readonly ILogger<AdminWalletsController> _logger;

    public AdminWalletsController(IAdminWalletService adminWalletService, ILogger<AdminWalletsController> logger)
    {
        _adminWalletService = adminWalletService;
        _logger = logger;
    }

    private string? GetCurrentUserId()
    {
        var headerValue = Request.Headers["x-user-id"].ToString();
        return string.IsNullOrWhiteSpace(headerValue) ? null : headerValue.Trim();
    }

    [HttpPatch("withdrawals/{id:guid}")]
    [ProducesResponseType(typeof(ProcessWithdrawalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ProcessWithdrawal(
        Guid id,
        [FromBody] ProcessWithdrawalRequest request,
        CancellationToken cancellationToken)
    {
        var adminUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(new { title = "Unauthorized", detail = "Admin user identifier is required.", errorCode = "UNAUTHORIZED" });
        }

        _logger.LogInformation("Processing withdrawal '{WithdrawalId}' by admin '{AdminId}' with action '{Action}'", id, adminUserId, request.Action);
        var result = await _adminWalletService.ProcessWithdrawalAsync(id, adminUserId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{walletId:guid}/adjustments")]
    [ProducesResponseType(typeof(BalanceAdjustmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AdjustBalance(
        Guid walletId,
        [FromBody] BalanceAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var adminUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(new { title = "Unauthorized", detail = "Admin user identifier is required.", errorCode = "UNAUTHORIZED" });
        }

        _logger.LogInformation("Adjusting wallet '{WalletId}' by admin '{AdminId}', Amount: {Amount}", walletId, adminUserId, request.Amount);
        var result = await _adminWalletService.AdjustBalanceAsync(walletId, adminUserId, request, cancellationToken);
        return Ok(result);
    }
}
