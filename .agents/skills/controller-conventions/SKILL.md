---
name: controller-conventions
description: Canonical standards for ASP.NET Core thin controllers in WalletService (port 8008). Covers the frozen MediatR/CQRS architecture, ISender injection, command enrichment, boundary validation, and controller blueprint.
---

# Controller Conventions & Standards in WalletService

> Scope: WalletService repository only. Never reference parent workspace paths, root documentation, or sibling service directories.
> Architectural Exception: WalletService is the frozen exception permitted to utilize MediatR and CQRS.

This skill defines the invariant structural and boundary rules for all REST API controllers in `Wallet.Api`.

Controllers serve strictly as **HTTP Transport Boundary Adapters**. They bind incoming HTTP protocol details, extract verified identities, instantiate Command or Query records, dispatch via `ISender`, and translate results into standardized HTTP responses. They must never contain business decisions, direct database queries, or manual error formatting.

---

## 0. Frozen Architecture Invariants

WalletService operates under an explicit architecture freeze:
- **Exactly 2 Controllers**: `WalletsController.cs` and `AdminWalletsController.cs` in `Wallet.Api/Controllers/Public/`. Prohibit splitting into per-resource controllers (`TopupsController`, `TransfersController`, `VnPayController`, etc.).
- **ISender Injection Only**: Controllers inject ONLY `ISender` and `ILogger<{Controller}>`. Prohibit injecting DbContext, repositories, or creating `IWalletService` facades.
- **Classic CQRS**: Exactly 11 use cases live at `Wallet.Application/Features/{Area}/{Commands|Queries}/{UseCase}/`.
- **Pre-Commit Verification**: Run `./scripts/verify-wallet-architecture.sh` to ensure CQRS layout integrity.

---

## 1. When to Use

Consult and follow this skill whenever you:
- Add action methods to `WalletsController.cs` or `AdminWalletsController.cs`.
- Review controller code changes for architectural consistency in WalletService.
- Bind request parameters (`[FromRoute]`, `[FromQuery]`, `[FromBody]`).
- Enrich commands with server-resolved claims (`UserId`, `ClientIp`).
- Format outbound responses, pagination metadata, or status codes.

---

## 2. Request and Response DTO Placement (CQRS Commands & Queries)

- **Declare Commands/Queries in Features**: Each Command or Query record lives directly in its feature folder (`Wallet.Application/Features/{Area}/Commands|Queries/{UseCase}/`).
- **Server Properties Use JsonIgnore**: Properties resolved on the server (`UserId`, `AdminUserId`, `ClientIp`) are marked with `[JsonIgnore]` on the Command record and populated via C# `with` expressions in the controller.
- **Direct Consumption by Controller**: Controllers directly bind these Command/Query records and return handler response records.
- **Domain Entities Stay Private**: Never expose EF Core entities (`Wallet`, `WalletTransaction`, `WalletTopup`) through the controller. Only immutable response records and `PagedData<T>` leave the application boundary.

---

## 3. Validation Placement (Boundary Discipline)

- **Boundary Validation on Command Records**: Place declarative validation attributes from `System.ComponentModel.DataAnnotations` directly on Command properties (`[Required]`, `[Range(1000, 100000000)]`, `[RegularExpression]`).
- **CQRS Pipeline Validation**: Commands automatically pass through `ValidationBehavior<TRequest, TResponse>` registered in MediatR pipeline.
- **Automatic Model State Rejection**: Invalid inputs throw `RequestValidationException`, intercepted to produce standardized RFC 9110 ProblemDetails with `errorCode = "VAL_INVALID_INPUT"`.
- **PROHIBIT Manual Validation in Controllers**: Never write ad-hoc validation checks inside controller action bodies (e.g. prohibited: `if (command.Amount <= 0) return BadRequest(...);`).

---

## 4. Error Message Standards (Descriptive Sentence Case)

When domain exceptions, validation failures, or financial balance guards trigger errors:

- **100% English Language**: All error messages, failure details, and exception descriptions must be written in English.
- **Sentence Case Format**:
  - Capitalize ONLY the first letter of the sentence.
  - All subsequent words must be lowercase, except technical acronyms and identifiers (`ID`, `OTP`, `JWT`, `URL`, `UUID`, `Email`, `API`, `HTTP`, `VND`).
  - Prohibit ALL-CAPS messages (e.g. prohibited: `"INSUFFICIENT BALANCE"`, `"WALLET NOT FOUND"`).
  - Prohibit Title Case (e.g. prohibited: `"Insufficient Balance"`).
- **Contextual Precision**: The message must precisely describe what condition failed so any developer or client can diagnose the issue immediately.
  - Correct: `"Wallet account not found for the specified user."`
  - Correct: `"Insufficient balance to complete the transaction."`
  - Correct: `"Deposit amount must be between 10,000 VND and 100,000,000 VND."`
- **Error Codes**: Always `SCREAMING_SNAKE_CASE` without arbitrary `ERR_` prefixes (`WALLET_NOT_FOUND`, `INSUFFICIENT_BALANCE`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`).

---

## 5. Canonical Reference Controller Blueprint

The following structural skeleton represents the standardized pattern for controllers in `Wallet.Api`:

```csharp
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
using Wallet.Application.Features.Topups.Commands.TopupVnPay;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;

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

    /// <summary>Gets own wallet account and balance.</summary>
    [HttpGet("me")]
    [RequireRole]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWallet(CancellationToken cancellationToken)
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TopupVnPay(
        [FromBody] TopupVnPayCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userGuid, out var authError))
        {
            return authError;
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var enrichedCommand = command with { UserId = userGuid.ToString(), ClientIp = clientIp };

        _logger.LogInformation("Initiating VNPay topup for user '{UserId}' with amount {Amount}", userGuid, command.Amount);
        var result = await _sender.Send(enrichedCommand, cancellationToken);

        return Ok(result);
    }
}
```

---

## 6. HTTP Status & Return Conventions

- **GET Single Item / Query**: Return `Task<IActionResult>` with `Ok(response)` (HTTP 200).
- **GET Paginated List**: Return `Task<IActionResult>` with `Ok(pagedData)` (HTTP 200).
- **POST Action**: Return `Task<IActionResult>` with `Ok(result)` or `StatusCode(StatusCodes.Status201Created, result)`.
- **DELETE or Empty Operations**: Return `Task<IActionResult>` with `NoContent()` (HTTP 204, 0-byte payload).
- **No Manual Outer Envelopes**: Microservice controllers return pure payloads. Wrapping into `ApiResponse<T>` is performed by Gateway `ResponseEnvelopeMiddleware`.
- **Automatic Error Responses**: All unhandled domain exceptions bubble up to `GlobalExceptionHandlerMiddleware` to format RFC 9110 ProblemDetails.
