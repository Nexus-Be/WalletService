namespace Wallet.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Wallet.Api.Common;
using Wallet.Api.Extensions;
using Wallet.Api.Filters;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Application.Features.Checkout.Queries.GetDirectPaymentStatus;
using Wallet.Application.Features.Holds.Commands.CreateHold;
using Wallet.Application.Features.Holds.Commands.CaptureHold;
using Wallet.Application.Features.Holds.Commands.RefundCapturedHold;
using Wallet.Application.Features.Holds.Commands.ReleaseHold;
using Wallet.Application.Features.Holds.Queries.GetHold;
using Wallet.Application.Features.Holds.Queries.ListMyHolds;
using Wallet.Application.Features.Stripe.Commands.ProcessStripeWebhook;
using Wallet.Application.Features.Topups.Commands.TopupStripe;
using Wallet.Application.Features.Topups.Commands.ResumeTopup;
using Wallet.Application.Features.Topups.Queries.ListTopups;
using Wallet.Application.Features.Topups.Queries.GetTopupStatus;
using Wallet.Application.Features.Transfers.Commands.Transfer;
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

    /// <summary>Reserves wallet funds for an auction deposit.</summary>
    [HttpPost("holds")]
    [RequireAuctionServiceKey]
    [ProducesResponseType(typeof(CreateHoldResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CreateHoldResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateHold([FromBody] CreateHoldCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(command with { IdempotencyKey = key ?? string.Empty }, cancellationToken);
        return StatusCode(result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Hold);
    }

    /// <summary>Releases an existing auction deposit hold.</summary>
    [HttpPost("holds/{holdId:guid}/release")]
    [RequireAuctionServiceKey]
    [ProducesResponseType(typeof(ReleaseHoldResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReleaseHold(Guid holdId, [FromBody] ReleaseHoldCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(command with { HoldId = holdId, IdempotencyKey = key ?? string.Empty }, cancellationToken));

    /// <summary>Captures an auction deposit hold.</summary>
    [HttpPost("holds/{holdId:guid}/capture")]
    [RequireAuctionServiceKey]
    [ReleaseFeatureAttribute(ReleaseFeature.AuctionOrderCapture)]
    [ProducesResponseType(typeof(CaptureHoldResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CaptureHold(Guid holdId, [FromBody] CaptureHoldCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(command with { HoldId = holdId, IdempotencyKey = key ?? string.Empty }, cancellationToken));

    /// <summary>Refunds a captured order deposit.</summary>
    [HttpPost("holds/{holdId:guid}/refund-captured")]
    [RequireAuctionServiceKey]
    [ProducesResponseType(typeof(RefundCapturedHoldResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RefundCapturedHold(Guid holdId, [FromBody] RefundCapturedHoldCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(command with { HoldId = holdId, IdempotencyKey = key ?? string.Empty }, cancellationToken));

    /// <summary>Gets an auction deposit hold by ID.</summary>
    [HttpGet("holds/{holdId:guid}")]
    [RequireAuctionServiceKey]
    [ProducesResponseType(typeof(HoldStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHold(Guid holdId,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetHoldQuery(holdId, null, null), cancellationToken));

    /// <summary>Gets an auction deposit hold by reference.</summary>
    [HttpGet("holds/by-reference/{referenceType}/{referenceId}")]
    [RequireAuctionServiceKey]
    [ProducesResponseType(typeof(HoldStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHoldByReference(string referenceType, string referenceId,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetHoldQuery(null, referenceType, referenceId), cancellationToken));

    /// <summary>Lists the authenticated user's auction deposit holds.</summary>
    [HttpGet("me/holds")]
    [RequireRole]
    [ProducesResponseType(typeof(Wallet.Application.Common.Models.PagedData<HoldListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMyHolds([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string? status = null, CancellationToken cancellationToken = default)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(new ListMyHoldsQuery(page, pageSize, status) { UserId = userGuid }, cancellationToken));
    }

    /// <summary>Initiates a Stripe wallet topup.</summary>
    [HttpPost("topups/stripe")]
    [RequireRole]
    [ProducesResponseType(typeof(TopupStripeResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> TopupStripe(
        [FromBody] TopupStripeCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        _logger.LogInformation("Processing deposit for user: '{UserId}', Amount: {Amount}", userGuid, command.Amount);
        var result = await _sender.Send(
            command with { UserId = userGuid, IdempotencyKey = idempotencyKey ?? string.Empty },
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Creates a pending withdrawal request and reserves available wallet funds.</summary>
    [HttpPost("withdrawals")]
    [RequireRole("seller")]
    [ReleaseFeatureAttribute(ReleaseFeature.Withdrawals)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
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

    /// <summary>Processes a signed Stripe webhook.</summary>
    [HttpPost("stripe/webhooks")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> StripeWebhook(
        [FromHeader(Name = "Stripe-Signature")] string? signature, CancellationToken cancellationToken)
    {
        var body = await StripeWebhookBodyReader.ReadAsync(Request.Body, Request.ContentLength, cancellationToken);
        await _sender.Send(new ProcessStripeWebhookCommand(body, signature ?? string.Empty),
            cancellationToken);
        return Ok();
    }

    /// <summary>Gets the authenticated user's Stripe topup status.</summary>
    [HttpGet("topups")]
    [RequireRole]
    [ProducesResponseType(typeof(Wallet.Application.Common.Models.PagedData<TopupListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTopups(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(new ListTopupsQuery(page, pageSize) { UserId = userGuid }, cancellationToken));
    }

    /// <summary>Returns an existing open Stripe Checkout URL for an owned topup.</summary>
    [HttpPost("topups/{id:guid}/resume")]
    [RequireRole]
    [ProducesResponseType(typeof(ResumeTopupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResumeTopup(Guid id, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(new ResumeTopupCommand(id) { UserId = userGuid }, cancellationToken));
    }

    /// <summary>Gets the authenticated user's Stripe topup status.</summary>
    [HttpGet("topups/{id:guid}")]
    [RequireRole]
    [ProducesResponseType(typeof(TopupStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopupStatus(Guid id, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        var result = await _sender.Send(new GetTopupStatusQuery(id) { UserId = userGuid }, cancellationToken);
        return Ok(result);
    }

    /// <summary>Initiates a Stripe direct order payment.</summary>
    [HttpPost("checkout/direct")]
    [RequireRole]
    [ProducesResponseType(typeof(DirectPaymentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckoutDirect(
        [FromBody] CheckoutDirectCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        _logger.LogInformation(
            "[REST Wallet] Initiating direct payment for user '{UserId}', order '{OrderId}'",
            userGuid,
            command.OrderId);
        var result = await _sender.Send(command with { UserId = userGuid }, cancellationToken);
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
    [ReleaseFeatureAttribute(ReleaseFeature.LegacyCommerceCredits)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
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
    [ReleaseFeatureAttribute(ReleaseFeature.LegacyCommerceCredits)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
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
    [ReleaseFeatureAttribute(ReleaseFeature.LegacyCommerceCredits)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
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
