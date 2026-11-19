namespace Wallet.Api.Controllers;

using System;
using System.Collections.Generic;
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
using Wallet.Application.Common.Models;
using Wallet.Application.Features.BankAccounts;
using Wallet.Application.Features.BankAccounts.Commands.CreateBankAccount;
using Wallet.Application.Features.BankAccounts.Commands.DeleteBankAccount;
using Wallet.Application.Features.BankAccounts.Commands.SetDefaultBankAccount;
using Wallet.Application.Features.BankAccounts.Queries.ListMyBankAccounts;
using Wallet.Application.Features.Checkout.Commands.AdjustOrderPayment;
using Wallet.Application.Features.Checkout.Commands.CancelDirectPayment;
using Wallet.Application.Features.Checkout.Commands.CancelOrderEscrow;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Checkout.Commands.ExpireDirectPayment;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;
using Wallet.Application.Features.Checkout.Commands.ResumeDirectPayment;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Application.Features.Checkout.Queries.GetDirectPaymentStatus;
using Wallet.Application.Features.Holds.Commands.CaptureHold;
using Wallet.Application.Features.Holds.Commands.CreateHold;
using Wallet.Application.Features.Holds.Commands.RefundCapturedHold;
using Wallet.Application.Features.Holds.Commands.ReleaseHold;
using Wallet.Application.Features.Holds.Queries.GetHold;
using Wallet.Application.Features.Holds.Queries.ListMyHolds;
using Wallet.Application.Features.Stripe.Commands.ProcessStripeWebhook;
using Wallet.Application.Features.Topups.Commands.ResumeTopup;
using Wallet.Application.Features.Topups.Commands.TopupStripe;
using Wallet.Application.Features.Topups.Queries.GetTopupStatus;
using Wallet.Application.Features.Topups.Queries.ListTopups;
using Wallet.Application.Features.Transfers.Commands.Transfer;
using Wallet.Application.Features.Wallets;
using Wallet.Application.Features.Wallets.Commands.FreezeWallet;
using Wallet.Application.Features.Wallets.Commands.UnfreezeWallet;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;
using Wallet.Application.Features.Wallets.Queries.GetTransactionById;
using Wallet.Application.Features.Wallets.Queries.ListTransactions;
using Wallet.Application.Features.Withdrawals;
using Wallet.Application.Features.Withdrawals.Commands.CancelWithdrawal;
using Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;
using Wallet.Application.Features.Withdrawals.Queries.GetMyWithdrawal;
using Wallet.Application.Features.Withdrawals.Queries.ListMyWithdrawals;

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
    [ProducesResponseType(typeof(PagedData<HoldListItem>), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(WithdrawalResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(WithdrawalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Withdraw(
        [FromBody] RequestWithdrawalCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        _logger.LogInformation("Processing withdrawal for user: '{UserId}', Amount: {Amount}", userGuid,
            command.Amount);
        var result = await _sender.Send(
            command with { UserId = userGuid.ToString(), IdempotencyKey = idempotencyKey?.Trim() ?? string.Empty },
            cancellationToken);
        return result.IsReplay ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Lists the authenticated seller's withdrawal requests.</summary>
    [HttpGet("withdrawals")]
    [RequireRole("seller")]
    [ProducesResponseType(typeof(PagedData<WithdrawalSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWithdrawals(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(
            new ListMyWithdrawalsQuery(page, pageSize, status) { UserId = userGuid },
            cancellationToken));
    }

    /// <summary>Gets a single withdrawal request owned by the authenticated seller.</summary>
    [HttpGet("withdrawals/{id:guid}")]
    [RequireRole("seller")]
    [ProducesResponseType(typeof(WithdrawalSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWithdrawal(Guid id, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(new GetMyWithdrawalQuery(id) { UserId = userGuid }, cancellationToken));
    }

    /// <summary>Cancels a pending withdrawal request owned by the authenticated seller.</summary>
    [HttpPost("withdrawals/{id:guid}/cancel")]
    [RequireRole("seller")]
    [ProducesResponseType(typeof(WithdrawalSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelWithdrawal(Guid id, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(new CancelWithdrawalCommand(id) { UserId = userGuid }, cancellationToken));
    }

    /// <summary>Lists saved bank accounts owned by the authenticated seller.</summary>
    [HttpGet("bank-accounts")]
    [RequireRole("seller")]
    [ProducesResponseType(typeof(IReadOnlyList<BankAccountResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListBankAccounts(CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(new ListMyBankAccountsQuery { UserId = userGuid }, cancellationToken));
    }

    /// <summary>Creates a saved bank account for the authenticated seller.</summary>
    [HttpPost("bank-accounts")]
    [RequireRole("seller")]
    [ProducesResponseType(typeof(BankAccountResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateBankAccount(
        [FromBody] CreateBankAccountCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        var created = await _sender.Send(command with { UserId = userGuid }, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Marks one of the authenticated seller's saved bank accounts as default.</summary>
    [HttpPatch("bank-accounts/{id:guid}/default")]
    [RequireRole("seller")]
    [ProducesResponseType(typeof(BankAccountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDefaultBankAccount(Guid id, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(new SetDefaultBankAccountCommand(id) { UserId = userGuid }, cancellationToken));
    }

    /// <summary>Deletes one of the authenticated seller's saved bank accounts.</summary>
    [HttpDelete("bank-accounts/{id:guid}")]
    [RequireRole("seller")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteBankAccount(Guid id, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        await _sender.Send(new DeleteBankAccountCommand(id) { UserId = userGuid }, cancellationToken);
        return NoContent();
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

    /// <summary>Gets details for a single wallet transaction owned by the caller.</summary>
    [HttpGet("transactions/{id:guid}")]
    [RequireRole]
    [ProducesResponseType(typeof(WalletTransactionDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTransaction(Guid id, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(new GetTransactionByIdQuery(id) { UserId = userGuid }, cancellationToken));
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

    /// <summary>Lists the authenticated user's Stripe topups.</summary>
    [HttpGet("topups")]
    [RequireRole]
    [ProducesResponseType(typeof(PagedData<TopupListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTopups(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(
            new ListTopupsQuery(page, pageSize, status) { UserId = userGuid },
            cancellationToken));
    }

    /// <summary>Returns an existing open Stripe Checkout URL for an owned topup.</summary>
    [HttpPost("topups/{id:guid}/resume")]
    [RequireRole]
    [ProducesResponseType(typeof(ResumeTopupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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

    /// <summary>Returns an existing open Stripe Checkout URL for an owned direct order payment.</summary>
    [HttpPost("checkout/direct/{orderId:guid}/resume")]
    [RequireRole]
    [ProducesResponseType(typeof(ResumeDirectPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResumeDirectPayment(Guid orderId, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(
            new ResumeDirectPaymentCommand(orderId) { UserId = userGuid },
            cancellationToken));
    }

    /// <summary>Cancels an unpaid pending Stripe direct order payment so the buyer can choose another payment method.</summary>
    [HttpPost("checkout/direct/{orderId:guid}/cancel")]
    [RequireRole]
    [ProducesResponseType(typeof(DirectPaymentLifecycleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelDirectPayment(Guid orderId, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError)) return authError;
        return Ok(await _sender.Send(
            new CancelDirectPaymentCommand(orderId) { UserId = userGuid },
            cancellationToken));
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

    /// <summary>Legacy protected route that debits the buyer's wallet for an order until Commerce migrates to internal routes.</summary>
    [HttpPost("orders/{orderId:guid}/payment")]
    [RequireInternalServiceKey]
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

    /// <summary>Legacy protected route that credits the seller's wallet when a sub-order payout is released.</summary>
    [HttpPost("sub-orders/{subOrderId:guid}/settle")]
    [RequireInternalServiceKey]
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

    /// <summary>Legacy protected route that refunds an order's wallet payment back to the buyer.</summary>
    [HttpPost("orders/{orderId:guid}/refund")]
    [RequireInternalServiceKey]
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

    /// <summary>Legacy protected route that refunds a sub-order's wallet payment back to the buyer.</summary>
    [HttpPost("sub-orders/{subOrderId:guid}/refund")]
    [RequireInternalServiceKey]
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

    // ---- Internal Service-to-Service Endpoints (Gateway never exposes /api/v1/internal/*) ----

    /// <summary>Gets a user's wallet balance, available balance, and freeze status for internal services.</summary>
    [HttpGet("~/api/v1/internal/wallets/{userId:guid}")]
    [RequireInternalServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserWalletInternal(Guid userId, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetMyWalletQuery(userId.ToString()), cancellationToken));

    /// <summary>Freezes a user's wallet during an open purchase dispute.</summary>
    [HttpPost("~/api/v1/internal/wallets/freeze-purchase")]
    [RequireInternalServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(WalletFreezeStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> FreezePurchaseInternal(
        [FromBody] InternalWalletFreezeRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new FreezeWalletCommand(request.UserId, request.Reason), cancellationToken));

    /// <summary>Unfreezes a user's wallet after a purchase dispute is resolved.</summary>
    [HttpPost("~/api/v1/internal/wallets/unfreeze-purchase")]
    [RequireInternalServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(WalletFreezeStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnfreezePurchaseInternal(
        [FromBody] InternalWalletFreezeRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new UnfreezeWalletCommand(request.UserId, request.Reason), cancellationToken));

    /// <summary>Debits a buyer's wallet for an order over internal service authentication.</summary>
    [HttpPost("~/api/v1/internal/wallets/orders/{orderId:guid}/payment")]
    [RequireInternalServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PayOrderInternal(
        Guid orderId,
        [FromBody] InternalPayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(
            new PayOrderWithWalletCommand(request.Amount) { OrderId = orderId, UserId = request.BuyerId },
            cancellationToken));

    /// <summary>Credits the seller's wallet when a sub-order payout is released over internal service authentication.</summary>
    [HttpPost("~/api/v1/internal/wallets/sub-orders/{subOrderId:guid}/settle")]
    [RequireInternalServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ReleaseFeatureAttribute(ReleaseFeature.LegacyCommerceCredits)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SettleSellerOrderInternal(
        Guid subOrderId,
        [FromBody] SettleSellerOrderCommand command,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(command with { SubOrderId = subOrderId }, cancellationToken));

    /// <summary>Refunds an order's wallet payment back to the buyer over internal service authentication.</summary>
    [HttpPost("~/api/v1/internal/wallets/orders/{orderId:guid}/refund")]
    [RequireInternalServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ReleaseFeatureAttribute(ReleaseFeature.LegacyCommerceCredits)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RefundOrderInternal(
        Guid orderId,
        [FromBody] RefundOrderPaymentCommand command,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(command with { OrderId = orderId }, cancellationToken));

    /// <summary>Refunds a sub-order's wallet payment back to the buyer over internal service authentication.</summary>
    [HttpPost("~/api/v1/internal/wallets/sub-orders/{subOrderId:guid}/refund")]
    [RequireInternalServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ReleaseFeatureAttribute(ReleaseFeature.LegacyCommerceCredits)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RefundSubOrderInternal(
        Guid subOrderId,
        [FromBody] RefundOrderPaymentCommand command,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(command with { OrderId = subOrderId, IsSubOrder = true }, cancellationToken));

    /// <summary>Expires an unpaid direct Stripe payment when the order expires in CommerceService.</summary>
    [HttpPost("~/api/v1/internal/wallets/checkout/direct/{orderId:guid}/expire")]
    [RequireInternalServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(DirectPaymentLifecycleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExpireDirectPaymentInternal(
        Guid orderId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new ExpireDirectPaymentCommand(orderId), cancellationToken));

    /// <summary>Adjusts an already-paid order's wallet escrow after a post-order shipping change.</summary>
    [HttpPost("~/api/v1/internal/wallets/orders/{orderId:guid}/adjust-payment")]
    [RequireInternalServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(WalletOrderTransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdjustOrderPaymentInternal(
        Guid orderId,
        [FromBody] AdjustOrderPaymentCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(
            command with { OrderId = orderId, IdempotencyKey = idempotencyKey?.Trim() ?? string.Empty },
            cancellationToken));

    /// <summary>Cancels an untouched wallet-payment order escrow and returns the full balance to the funding wallet.</summary>
    [HttpPost("~/api/v1/internal/wallets/orders/{orderId:guid}/escrow/cancel")]
    [RequireInternalServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(CancelOrderEscrowResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelOrderEscrowInternal(
        Guid orderId,
        [FromBody] CancelOrderEscrowCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(
            command with { OrderId = orderId, IdempotencyKey = idempotencyKey?.Trim() ?? string.Empty },
            cancellationToken));

    /// <summary>Creates an auction deposit hold on the internal service namespace.</summary>
    [HttpPost("~/api/v1/internal/wallets/holds")]
    [RequireAuctionServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(CreateHoldResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CreateHoldResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateHoldInternal([FromBody] CreateHoldCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(command with { IdempotencyKey = key ?? string.Empty }, cancellationToken);
        return StatusCode(result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Hold);
    }

    /// <summary>Releases an auction deposit hold on the internal service namespace.</summary>
    [HttpPost("~/api/v1/internal/wallets/holds/{holdId:guid}/release")]
    [RequireAuctionServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(ReleaseHoldResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReleaseHoldInternal(Guid holdId, [FromBody] ReleaseHoldCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(command with { HoldId = holdId, IdempotencyKey = key ?? string.Empty }, cancellationToken));

    /// <summary>Captures an auction deposit hold on the internal service namespace.</summary>
    [HttpPost("~/api/v1/internal/wallets/holds/{holdId:guid}/capture")]
    [RequireAuctionServiceKey]
    [ReleaseFeatureAttribute(ReleaseFeature.AuctionOrderCapture)]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(CaptureHoldResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CaptureHoldInternal(Guid holdId, [FromBody] CaptureHoldCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(command with { HoldId = holdId, IdempotencyKey = key ?? string.Empty }, cancellationToken));

    /// <summary>Refunds a captured order deposit on the internal service namespace.</summary>
    [HttpPost("~/api/v1/internal/wallets/holds/{holdId:guid}/refund-captured")]
    [RequireAuctionServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(RefundCapturedHoldResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RefundCapturedHoldInternal(Guid holdId, [FromBody] RefundCapturedHoldCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(command with { HoldId = holdId, IdempotencyKey = key ?? string.Empty }, cancellationToken));

    /// <summary>Gets an auction deposit hold by ID on the internal service namespace.</summary>
    [HttpGet("~/api/v1/internal/wallets/holds/{holdId:guid}")]
    [RequireAuctionServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(HoldStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHoldInternal(Guid holdId,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetHoldQuery(holdId, null, null), cancellationToken));

    /// <summary>Gets an auction deposit hold by reference on the internal service namespace.</summary>
    [HttpGet("~/api/v1/internal/wallets/holds/by-reference/{referenceType}/{referenceId}")]
    [RequireAuctionServiceKey]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(HoldStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHoldByReferenceInternal(string referenceType, string referenceId,
        [FromHeader(Name = "X-Internal-Service-Key")] string? serviceKey, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetHoldQuery(null, referenceType, referenceId), cancellationToken));
}

public sealed record InternalWalletFreezeRequest(Guid UserId, string? Reason);

public sealed record InternalPayOrderRequest(string BuyerId, decimal Amount);
