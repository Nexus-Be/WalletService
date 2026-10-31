namespace Wallet.Api.Controllers.Internal;

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;

[ApiController]
[Route("api/v1/internal/wallets")]
public sealed class InternalWalletsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<InternalWalletsController> _logger;

    public InternalWalletsController(ISender sender, ILogger<InternalWalletsController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>Processes direct order checkout payment from user wallet.</summary>
    [HttpPost("checkout/direct")]
    public async Task<ActionResult<DirectPaymentResponse>> CheckoutDirect(
        [FromBody] CheckoutDirectCommand command,
        [FromHeader(Name = "x-user-id")] string? userIdHeader,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[REST Internal Wallet] Initiating direct payment for user '{UserId}', order '{OrderId}'",
            userIdHeader,
            command.OrderId);
        var result = await _sender.Send(
            command with { UserId = userIdHeader ?? string.Empty },
            cancellationToken);
        return Ok(result);
    }
}
