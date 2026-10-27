namespace Wallet.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Wallet.Application.DTOs;
using Wallet.Application.Features.Commands.Deposit;
using Wallet.Application.Features.Commands.Transfer;
using Wallet.Application.Features.Commands.Withdraw;
using Wallet.Application.Features.Queries.GetWallet;
using Wallet.Application.Features.Queries.ListTransactions;

[ApiController]
[Route("api/v1/wallets")]
public sealed class WalletsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<WalletsController> _logger;

    public WalletsController(IMediator mediator, ILogger<WalletsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    private string? GetCurrentUserId()
    {
        var headerValue = Request.Headers["x-user-id"].ToString();
        return string.IsNullOrWhiteSpace(headerValue) ? null : headerValue.Trim();
    }

    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMyWallet(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { title = "Invalid user identifier", detail = "Missing or empty x-user-id header.", errorCode = "VAL_INVALID_USER_ID" });
        }

        _logger.LogInformation("[REST GetMyWallet] Fetching wallet for UserId: '{UserId}'", userId);
        var wallet = await _mediator.Send(new GetWalletQuery(userId), cancellationToken);

        return Ok(new
        {
            walletId = wallet.Id,
            userId = wallet.UserId,
            balance = wallet.Balance,
            availableBalance = wallet.Balance,
            lockedBalance = 0m,
            currency = wallet.Currency,
            status = wallet.Status,
            updatedAt = wallet.UpdatedAt
        });
    }

    [HttpPost("topups/vnpay")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TopupVnPay([FromBody] TopupApiRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { title = "Invalid user identifier", detail = "Missing or empty x-user-id header.", errorCode = "VAL_INVALID_USER_ID" });
        }

        if (request.Amount < 10000m)
        {
            return BadRequest(new { title = "Invalid amount", detail = "Deposit amount must be at least 10,000 VND.", errorCode = "VAL_INVALID_AMOUNT" });
        }

        _logger.LogInformation("[REST TopupVnPay] Processing deposit for UserId: '{UserId}', Amount: {Amount}", userId, request.Amount);
        var result = await _mediator.Send(new DepositCommand(userId, request.Amount, request.Currency ?? "VND", request.PaymentMethod ?? "VNPAY"), cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            transactionId = result.Id,
            paymentUrl = $"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?orderId={result.Id}&amount={request.Amount}",
            amount = result.Amount,
            currency = result.Currency,
            status = result.Status,
            createdAt = result.CreatedAt
        });
    }

    [HttpPost("withdrawals")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Withdraw([FromBody] WithdrawalApiRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { title = "Invalid user identifier", detail = "Missing or empty x-user-id header.", errorCode = "VAL_INVALID_USER_ID" });
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { title = "Invalid amount", detail = "Withdrawal amount must be greater than zero.", errorCode = "VAL_INVALID_AMOUNT" });
        }

        _logger.LogInformation("[REST Withdraw] Processing withdrawal for UserId: '{UserId}', Amount: {Amount}", userId, request.Amount);
        var bankAccount = $"{request.BankCode}:{request.AccountNumber}".Trim(':');
        var result = await _mediator.Send(new WithdrawCommand(userId, request.Amount, "VND", bankAccount), cancellationToken);

        return Ok(new
        {
            transactionId = result.Id,
            amount = result.Amount,
            currency = result.Currency,
            status = result.Status,
            createdAt = result.CreatedAt
        });
    }

    [HttpPost("transfers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Transfer([FromBody] TransferApiRequest request, CancellationToken cancellationToken)
    {
        var senderUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(senderUserId))
        {
            return BadRequest(new { title = "Invalid user identifier", detail = "Missing or empty x-user-id header.", errorCode = "VAL_INVALID_USER_ID" });
        }

        if (string.IsNullOrWhiteSpace(request.RecipientUserId))
        {
            return BadRequest(new { title = "Invalid recipient", detail = "Recipient user ID is required.", errorCode = "VAL_INVALID_INPUT" });
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { title = "Invalid amount", detail = "Transfer amount must be greater than zero.", errorCode = "VAL_INVALID_AMOUNT" });
        }

        _logger.LogInformation("[REST Transfer] Transferring from '{Sender}' to '{Recipient}', Amount: {Amount}", senderUserId, request.RecipientUserId, request.Amount);
        var result = await _mediator.Send(new TransferCommand(senderUserId, request.RecipientUserId, request.Amount, request.Note ?? string.Empty), cancellationToken);

        return Ok(new
        {
            transactionId = result.Id,
            amount = result.Amount,
            currency = result.Currency,
            status = result.Status,
            createdAt = result.CreatedAt
        });
    }

    [HttpGet("transactions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
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

        _logger.LogInformation("[REST ListTransactions] Querying transactions for UserId: '{UserId}', Page: {Page}", userId, page);
        var result = await _mediator.Send(new ListTransactionsQuery(userId, page, pageSize, type), cancellationToken);

        return Ok(new
        {
            items = result.Items,
            totalItems = result.Total,
            pageNumber = page,
            pageSize = pageSize,
            totalPages = (int)Math.Ceiling((double)result.Total / pageSize)
        });
    }
}

public sealed record TopupApiRequest(
    decimal Amount,
    string? BankCode = null,
    string? ReturnUrl = null,
    string? Currency = null,
    string? PaymentMethod = null
);

public sealed record WithdrawalApiRequest(
    decimal Amount,
    string? BankCode = null,
    string? AccountNumber = null,
    string? AccountHolderName = null
);

public sealed record TransferApiRequest(
    string RecipientUserId,
    decimal Amount,
    string? Note = null
);
