namespace Wallet.Api.Controllers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Wallet.Api.Extensions;
using Wallet.Api.Filters;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Application.Features.Checkout.Queries.GetDirectPaymentStatus;
using Wallet.Application.Features.Topups.Commands.TopupVnPay;
using Wallet.Application.Features.Transfers.Commands.Transfer;
using Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;
using Wallet.Application.Features.VnPay.Queries.ProcessVnPayReturn;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;
using Wallet.Application.Features.Wallets.Queries.ListTransactions;
using Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;

[ApiController]
[Route("api/v1/wallets")]
public sealed class WalletsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<WalletsController> _logger;

    public WalletsController(ISender sender, ILogger<WalletsController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>Gets the authenticated user's wallet account and balance.</summary>
    /// <param name="userIdHeader">User ID forwarded in the x-user-id header.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    [HttpGet("me")]
    [RequireRole]
    public async Task<IActionResult> GetMyWallet(
        [FromHeader(Name = "x-user-id")] string? userIdHeader,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        _logger.LogInformation("Fetching wallet for user: '{UserId}'", userGuid);
        var wallet = await _sender.Send(new GetMyWalletQuery(userGuid.ToString()), cancellationToken);
        return Ok(wallet);
    }

    /// <summary>Initiates VNPay wallet topup transaction.</summary>
    [HttpPost("topups/vnpay")]
    [RequireRole]
    public async Task<IActionResult> TopupVnPay(
        [FromBody] TopupVnPayCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        var clientIp = this.GetClientIp() ?? "127.0.0.1";
        _logger.LogInformation("Processing deposit for user: '{UserId}', Amount: {Amount}", userGuid, command.Amount);
        var result = await _sender.Send(
            command with { UserId = userGuid.ToString(), ClientIp = clientIp },
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Creates a pending withdrawal request and reserves available wallet funds.</summary>
    [HttpPost("withdrawals")]
    [RequireRole("seller")]
    public async Task<IActionResult> Withdraw(
        [FromBody] RequestWithdrawalCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        _logger.LogInformation("Processing withdrawal for user: '{UserId}', Amount: {Amount}", userGuid,
            command.Amount);
        var result = await _sender.Send(command with { UserId = userGuid.ToString() }, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Transfers balance between wallet accounts.</summary>
    [HttpPost("transfers")]
    [RequireRole]
    public async Task<IActionResult> Transfer(
        [FromBody] TransferCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        _logger.LogInformation(
            "Transferring from '{Sender}' to '{Recipient}', Amount: {Amount}",
            userGuid,
            command.RecipientUserId,
            command.Amount);
        var result = await _sender.Send(command with { SenderUserId = userGuid.ToString() }, cancellationToken);
        return Ok(result);
    }

    /// <summary>Lists transactions for own wallet.</summary>
    [HttpGet("transactions")]
    [RequireRole]
    public async Task<IActionResult> ListTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? type = null,
        [FromQuery] DateTimeOffset? fromDate = null,
        [FromQuery] DateTimeOffset? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        _logger.LogInformation("Querying transactions for user: '{UserId}', page: {Page}", userGuid, page);
        var result = await _sender.Send(
            new ListTransactionsQuery(page, pageSize, type, fromDate, toDate) { UserId = userGuid.ToString() },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Processes payment webhook callback from VNPay.</summary>
    [HttpPost("vnpay/ipn")]
    public async Task<IActionResult> VnPayIpn(CancellationToken cancellationToken)
    {
        Dictionary<string, string> parameters;
        try
        {
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(cancellationToken);
                parameters = form.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                using var document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
                parameters = document.RootElement.EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => p.Value.ValueKind == JsonValueKind.String
                        ? p.Value.GetString() ?? string.Empty
                        : p.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            _logger.LogWarning(exception, "Invalid VNPay IPN request payload.");
            return Ok(new VnPayIpnResponse("99", "Invalid request payload"));
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _sender.Send(new ProcessVnPayIpnCommand(parameters, clientIp), cancellationToken);
        return Ok(result);
    }

    /// <summary>Handles browser return callback from VNPay after payment completion.</summary>
    [HttpGet("vnpay/return")]
    public async Task<IActionResult> VnPayReturn(CancellationToken cancellationToken)
    {
        var parameters = Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
        var result = await _sender.Send(new ProcessVnPayReturnQuery(parameters), cancellationToken);
        if (!result.Success && result.ResponseCode == "97")
        {
            return BadRequest(new { errorCode = "VAL_INVALID_SIGNATURE", message = result.Message });
        }

        return Ok(result);
    }

    /// <summary>Processes direct order checkout payment from the user wallet.</summary>
    [HttpPost("checkout/direct")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DirectPaymentResponse>> CheckoutDirect(
        [FromBody] CheckoutDirectCommand command,
        [FromHeader(Name = "x-user-id")] string? userIdHeader,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[REST Wallet] Initiating direct payment for user '{UserId}', order '{OrderId}'",
            userIdHeader,
            command.OrderId);
        var result = await _sender.Send(
            command with
            {
                UserId = userIdHeader ?? string.Empty,
                ClientIp = this.GetClientIp() ?? "127.0.0.1"
            },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Gets the status of a direct order payment so the caller can poll for the gateway result.</summary>
    [HttpGet("checkout/direct/{orderId}")]
    [RequireRole]
    [ProducesResponseType(typeof(DirectPaymentStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDirectPaymentStatus(
        string orderId,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        var result = await _sender.Send(
            new GetDirectPaymentStatusQuery(orderId)
            {
                UserId = userGuid.ToString(),
                AllowAnyUser = HttpContext.IsAdmin() || HttpContext.HasRole("staff")
            },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Debits the authenticated buyer's wallet for an order (called by Commerce).</summary>
    [HttpPost("orders/{orderId:guid}/payment")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PayOrder(
        Guid orderId,
        [FromBody] PayOrderWithWalletCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId, out var error))
        {
            return error;
        }

        var result = await _sender.Send(
            command with { OrderId = orderId, UserId = userId.ToString() },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Credits the seller's wallet when a sub-order payout is released (called by Commerce).</summary>
    [HttpPost("sub-orders/{subOrderId:guid}/settle")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SettleSellerOrder(
        Guid subOrderId,
        [FromBody] SettleSellerOrderCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(command with { SubOrderId = subOrderId }, cancellationToken);
        return Ok(result);
    }

    /// <summary>Refunds an order's wallet payment back to the buyer (called by Commerce).</summary>
    [HttpPost("orders/{orderId:guid}/refund")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RefundOrder(
        Guid orderId,
        [FromBody] RefundOrderPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(command with { OrderId = orderId }, cancellationToken);
        return Ok(result);
    }

    /// <summary>Refunds a sub-order's wallet payment back to the buyer (called by Commerce).</summary>
    [HttpPost("sub-orders/{subOrderId:guid}/refund")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RefundSubOrder(
        Guid subOrderId,
        [FromBody] RefundOrderPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            command with { OrderId = subOrderId, IsSubOrder = true },
            cancellationToken);
        return Ok(result);
    }
}
