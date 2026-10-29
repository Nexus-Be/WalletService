namespace Wallet.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Wallet.Application.Features.Commands.WalletOperations;

[ApiController]
[Route("api/v1/admin/wallets")]
public sealed class AdminWalletsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AdminWalletsController> _logger;

    public AdminWalletsController(IMediator mediator, ILogger<AdminWalletsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    private string? GetCurrentUserId()
    {
        var headerValue = Request.Headers["x-user-id"].ToString();
        return string.IsNullOrWhiteSpace(headerValue) ? null : headerValue.Trim();
    }

    [HttpPatch("withdrawals/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ProcessWithdrawal(
        Guid id,
        [FromBody] ProcessWithdrawalHttpRequest request,
        CancellationToken cancellationToken)
    {
        var adminUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(new { title = "Unauthorized", detail = "Admin user identifier is required.", errorCode = "UNAUTHORIZED" });
        }

        _logger.LogInformation("[REST Admin ProcessWithdrawal] Processing withdrawal '{WithdrawalId}' by admin '{AdminId}' with action '{Action}'", id, adminUserId, request.Action);
        var result = await _mediator.Send(
            new ProcessWithdrawalCommand(
                id.ToString(),
                adminUserId,
                request.Action,
                request.AdminNote),
            cancellationToken);

        return Ok(new
        {
            withdrawalId = result.WithdrawalId,
            status = result.Status,
            processedAt = result.ProcessedAt,
            transactionId = result.TransactionId
        });
    }

    [HttpPost("{walletId:guid}/adjustments")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AdjustBalance(
        Guid walletId,
        [FromBody] BalanceAdjustmentHttpRequest request,
        CancellationToken cancellationToken)
    {
        var adminUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(new { title = "Unauthorized", detail = "Admin user identifier is required.", errorCode = "UNAUTHORIZED" });
        }

        _logger.LogInformation("[REST Admin AdjustBalance] Adjusting wallet '{WalletId}' by admin '{AdminId}', Amount: {Amount}", walletId, adminUserId, request.Amount);
        var result = await _mediator.Send(
            new AdjustWalletBalanceCommand(
                walletId.ToString(),
                adminUserId,
                request.Amount,
                request.AdjustmentType,
                request.Reason),
            cancellationToken);

        return Ok(new
        {
            adjustmentId = result.Id,
            walletId = result.WalletId,
            amount = result.Amount,
            balanceAfter = result.BalanceAfter,
            createdAt = result.CreatedAt
        });
    }
}

public sealed record ProcessWithdrawalHttpRequest(string Action, string? AdminNote);

public sealed record BalanceAdjustmentHttpRequest(decimal Amount, string AdjustmentType, string Reason);
