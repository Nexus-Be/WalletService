namespace Wallet.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Wallet.Api.Extensions;
using Wallet.Application.Services.Wallet;

[ApiController]
[Route("api/v1/wallets")]
public sealed class WalletsController : ControllerBase
{
    private readonly IWalletService _walletService;
    private readonly ILogger<WalletsController> _logger;

    public WalletsController(IWalletService walletService, ILogger<WalletsController> logger)
    {
        _walletService = walletService;
        _logger = logger;
    }

    private string? GetCurrentUserId()
    {
        return this.GetUserId()?.ToString()
            ?? (Request.Headers.TryGetValue("x-user-id", out var headerValue) && !string.IsNullOrWhiteSpace(headerValue.ToString())
                ? headerValue.ToString().Trim()
                : null);
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMyWallet(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { title = "Invalid user identifier", detail = "Missing or empty x-user-id header.", errorCode = "VAL_INVALID_USER_ID" });
        }

        _logger.LogInformation("Fetching wallet for user: '{UserId}'", userId);
        var wallet = await _walletService.GetWalletByUserIdAsync(userId, cancellationToken);
        return Ok(wallet);
    }

    [HttpPost("topups/vnpay")]
    [ProducesResponseType(typeof(TopupResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TopupVnPay([FromBody] TopupVnPayRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { title = "Invalid user identifier", detail = "Missing or empty x-user-id header.", errorCode = "VAL_INVALID_USER_ID" });
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        _logger.LogInformation("Processing deposit for user: '{UserId}', Amount: {Amount}", userId, request.Amount);
        var result = await _walletService.TopupVnPayAsync(userId, request, clientIp, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("withdrawals")]
    [ProducesResponseType(typeof(WithdrawalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Withdraw([FromBody] WithdrawalRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { title = "Invalid user identifier", detail = "Missing or empty x-user-id header.", errorCode = "VAL_INVALID_USER_ID" });
        }

        _logger.LogInformation("Processing withdrawal for user: '{UserId}', Amount: {Amount}", userId, request.Amount);
        var result = await _walletService.WithdrawAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("transfers")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Transfer([FromBody] TransferRequest request, CancellationToken cancellationToken)
    {
        var senderUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(senderUserId))
        {
            return BadRequest(new { title = "Invalid user identifier", detail = "Missing or empty x-user-id header.", errorCode = "VAL_INVALID_USER_ID" });
        }

        _logger.LogInformation("Transferring from '{Sender}' to '{Recipient}', Amount: {Amount}", senderUserId, request.RecipientUserId, request.Amount);
        var result = await _walletService.TransferAsync(senderUserId, request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("transactions")]
    [ProducesResponseType(typeof(WalletTransactionPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? type = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { title = "Invalid user identifier", detail = "Missing or empty x-user-id header.", errorCode = "VAL_INVALID_USER_ID" });
        }

        var request = new ListTransactionsRequest(page, pageSize, type);
        _logger.LogInformation("Querying transactions for user: '{UserId}', page: {Page}", userId, page);
        var result = await _walletService.ListTransactionsAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("checkout/direct")]
    [ProducesResponseType(typeof(DirectPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CheckoutDirect([FromBody] DirectPaymentRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { title = "Invalid user identifier", detail = "Missing or empty x-user-id header.", errorCode = "VAL_INVALID_USER_ID" });
        }

        _logger.LogInformation("Initiating direct payment for user '{UserId}', order '{OrderId}'", userId, request.OrderId);
        var result = await _walletService.CheckoutDirectAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("vnpay/ipn")]
    [ProducesResponseType(typeof(VnPayIpnResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> VnPayIpn(CancellationToken cancellationToken)
    {
        Dictionary<string, string> parameters;
        try
        {
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(cancellationToken);
                parameters = form.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                using var document = await System.Text.Json.JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
                parameters = document.RootElement.EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            return Ok(new VnPayIpnResponse("99", "Invalid request payload"));
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var request = new ProcessVnPayIpnRequest(parameters, clientIp);
        var result = await _walletService.ProcessVnPayIpnAsync(request, cancellationToken);
        return Ok(result);
    }
}
