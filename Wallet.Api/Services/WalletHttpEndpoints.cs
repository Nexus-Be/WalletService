namespace Wallet.Api.Services;

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Wallet.Application.Features.Commands.Topups;
using Wallet.Application.Features.Commands.WalletOperations;
using Wallet.Application.Features.Queries.GetWallet;
using Wallet.Application.Features.Queries.ListTransactions;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

public static class WalletHttpEndpoints
{
    public static IEndpointRouteBuilder MapWalletHttpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/wallets/me", async (
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var identity = GetAuthenticatedUserId(httpContext, "BUYER", "SELLER", "ADMIN");
            if (identity.Failure is not null)
                return identity.Failure;

            return await Execute(httpContext, async () =>
            {
                var wallet = await mediator.Send(new GetWalletQuery(identity.UserId!), cancellationToken);
                if (!string.Equals(wallet.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    throw new WalletLockedException();

                return new
                {
                    walletId = wallet.Id,
                    userId = wallet.UserId,
                    balance = wallet.Balance,
                    lockedBalance = wallet.LockedBalance,
                    availableBalance = wallet.AvailableBalance,
                    currency = wallet.Currency,
                    status = wallet.Status,
                    updatedAt = wallet.UpdatedAt
                };
            });
        });

        endpoints.MapPost("/api/v1/wallets/topups/vnpay", async (
            VnPayTopupHttpRequest request,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var identity = GetAuthenticatedUserId(httpContext, "BUYER", "SELLER");
            if (identity.Failure is not null)
                return identity.Failure;
            if (string.IsNullOrWhiteSpace(request.ReturnUrl) ||
                !Uri.TryCreate(request.ReturnUrl, UriKind.Absolute, out var topupReturnUri) ||
                topupReturnUri.Scheme != Uri.UriSchemeHttps)
                return Error(httpContext, StatusCodes.Status400BadRequest, "VAL_INVALID_INPUT",
                    "Bad request", "returnUrl must be an absolute HTTPS URL.");

            var ipAddress = GetClientIpAddress(httpContext);
            if (ipAddress is null)
                return Error(httpContext, StatusCodes.Status400BadRequest, "VAL_INVALID_INPUT",
                    "Bad request", "A valid client IP address is required.");

            return await Execute(httpContext, async () =>
            {
                var result = await mediator.Send(
                    new InitiateVnPayTopupCommand(
                        identity.UserId!,
                        request.Amount,
                        ipAddress,
                        request.BankCode,
                        request.ReturnUrl),
                    cancellationToken);
                return new
                {
                    topupId = result.TopupId,
                    paymentUrl = result.PaymentUrl,
                    txnRef = result.TransactionReference,
                    expiresIn = 900
                };
            }, StatusCodes.Status201Created, "VAL_INVALID_AMOUNT");
        });

        endpoints.MapPost("/api/v1/wallets/vnpay/ipn", async (
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            Dictionary<string, string> parameters;
            try
            {
                parameters = await ReadVnPayParametersAsync(httpContext, cancellationToken);
            }
            catch (JsonException)
            {
                return VnPayResponse("97", "Invalid checksum hash.");
            }
            catch (InvalidOperationException)
            {
                return VnPayResponse("97", "Invalid checksum hash.");
            }

            var result = await mediator.Send(
                new ProcessVnPayIpnCommand(parameters, httpContext.Connection.RemoteIpAddress?.ToString()),
                cancellationToken);
            var message = result.ResponseCode switch
            {
                "00" => "Confirm success.",
                "01" => "Order not found.",
                "02" => "Order already confirmed.",
                "04" => "Invalid amount.",
                _ => "Invalid checksum hash."
            };
            return VnPayResponse(result.ResponseCode, message);
        });

        endpoints.MapPost("/api/v1/wallets/checkout/direct", async (
            DirectPaymentHttpRequest request,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var identity = GetAuthenticatedUserId(httpContext, "BUYER", "SELLER");
            if (identity.Failure is not null)
                return identity.Failure;
            if (string.IsNullOrWhiteSpace(request.ReturnUrl) ||
                !Uri.TryCreate(request.ReturnUrl, UriKind.Absolute, out var paymentReturnUri) ||
                paymentReturnUri.Scheme != Uri.UriSchemeHttps)
                return Error(httpContext, StatusCodes.Status400BadRequest, "VAL_INVALID_INPUT",
                    "Bad request", "returnUrl must be an absolute HTTPS URL.");

            return await Execute(httpContext, async () =>
            {
                var result = await mediator.Send(
                    new InitiateDirectGatewayPaymentCommand(
                        identity.UserId!,
                        request.OrderId,
                        request.Amount,
                        request.Gateway,
                        request.BankCode,
                        request.ReturnUrl),
                    cancellationToken);
                return new
                {
                    transactionId = result.TransactionId,
                    orderId = request.OrderId,
                    amount = request.Amount,
                    paymentUrl = result.PaymentUrl,
                    status = result.Status,
                    createdAt = DateTimeOffset.UtcNow
                };
            }, amountValidationErrorCode: "VAL_INVALID_AMOUNT");
        });

        endpoints.MapPost("/api/v1/wallets/withdrawals", async (
            WithdrawalHttpRequest request,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var identity = GetAuthenticatedUserId(httpContext, "SELLER");
            if (identity.Failure is not null)
                return identity.Failure;

            return await Execute(httpContext, async () =>
            {
                var result = await mediator.Send(
                    new RequestWithdrawalCommand(
                        identity.UserId!,
                        request.Amount,
                        request.BankCode,
                        request.AccountNumber,
                        request.AccountHolderName),
                    cancellationToken);
                return new
                {
                    withdrawalId = result.WithdrawalId,
                    amount = result.Amount,
                    fee = result.Fee,
                    status = result.Status,
                    requestedAt = result.RequestedAt
                };
            }, StatusCodes.Status201Created);
        });

        endpoints.MapPatch("/api/v1/admin/wallets/withdrawals/{id:guid}", async (
            Guid id,
            ProcessWithdrawalHttpRequest request,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var identity = GetAuthenticatedUserId(httpContext, "ADMIN", "SUPPORT_STAFF");
            if (identity.Failure is not null)
                return identity.Failure;

            return await Execute(httpContext, async () =>
            {
                var result = await mediator.Send(
                    new ProcessWithdrawalCommand(
                        id.ToString(),
                        identity.UserId!,
                        request.Action,
                        request.AdminNote),
                    cancellationToken);
                return new
                {
                    withdrawalId = result.WithdrawalId,
                    status = result.Status,
                    processedAt = result.ProcessedAt,
                    transactionId = result.TransactionId
                };
            });
        });

        endpoints.MapGet("/api/v1/wallets/transactions", async (
            int? page,
            int? limit,
            string? type,
            DateTimeOffset? fromDate,
            DateTimeOffset? toDate,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var identity = GetAuthenticatedUserId(httpContext, "BUYER", "SELLER", "ADMIN");
            if (identity.Failure is not null)
                return identity.Failure;
            if (page is <= 0 || limit is <= 0 || limit > 100)
                return Error(httpContext, StatusCodes.Status400BadRequest, "VAL_INVALID_INPUT",
                    "Bad request", "Page must be greater than zero and limit must be between 1 and 100.");

            return await Execute(httpContext, async () =>
            {
                var result = await mediator.Send(
                    new ListTransactionsQuery(
                        identity.UserId!,
                        page ?? 1,
                        limit ?? 20,
                        type,
                        fromDate,
                        toDate),
                    cancellationToken);
                var pageCount = result.PageSize == 0
                    ? 0
                    : (int)Math.Ceiling(result.Total / (double)result.PageSize);
                return new
                {
                    items = result.Items.Select(transaction => new
                    {
                        id = transaction.Id,
                        type = ToContractTransactionType(transaction.Type),
                        amount = IsDebit(transaction.Type) ? -transaction.Amount : transaction.Amount,
                        balanceAfter = transaction.BalanceAfter,
                        status = transaction.Status,
                        note = transaction.Note,
                        createdAt = transaction.CreatedAt
                    }),
                    pagination = new
                    {
                        pageIndex = result.Page,
                        pageSize = result.PageSize,
                        totalCount = result.Total,
                        totalPages = pageCount,
                        hasPreviousPage = result.Page > 1,
                        hasNextPage = result.Page < pageCount
                    }
                };
            }, amountValidationErrorCode: "VAL_INVALID_AMOUNT");
        });

        endpoints.MapPost("/api/v1/admin/wallets/{walletId:guid}/adjustments", async (
            Guid walletId,
            BalanceAdjustmentHttpRequest request,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var identity = GetAuthenticatedUserId(httpContext, "ADMIN");
            if (identity.Failure is not null)
                return identity.Failure;

            return await Execute(httpContext, async () =>
            {
                var result = await mediator.Send(
                    new AdjustWalletBalanceCommand(
                        walletId.ToString(),
                        identity.UserId!,
                        request.Amount,
                        request.AdjustmentType,
                        request.Reason),
                    cancellationToken);
                return new
                {
                    adjustmentId = result.Id,
                    walletId = result.WalletId,
                    amount = result.Amount,
                    balanceAfter = result.BalanceAfter,
                    createdAt = result.CreatedAt
                };
            });
        });

        return endpoints;
    }

    private static async Task<IResult> Execute<T>(
        HttpContext httpContext,
        Func<Task<T>> operation,
        int successStatusCode = StatusCodes.Status200OK,
        string amountValidationErrorCode = "VAL_INVALID_AMOUNT")
    {
        try
        {
            var data = await operation();
            return Results.Json(
                new ApiResponse<T>(
                    true,
                    successStatusCode,
                    null,
                    data,
                    GetTraceId(httpContext),
                    DateTimeOffset.UtcNow),
                statusCode: successStatusCode);
        }
        catch (ValidationException exception)
        {
            var errors = exception.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
            var amountError = exception.Errors.Any(error =>
                string.Equals(error.PropertyName, "Amount", StringComparison.OrdinalIgnoreCase));
            var minimumWithdrawalError = exception.Errors.Any(error =>
                error.ErrorMessage.Contains("Minimum withdrawal amount", StringComparison.OrdinalIgnoreCase));
            var errorCode = minimumWithdrawalError
                ? "MIN_WITHDRAWAL_LIMIT"
                : amountError ? amountValidationErrorCode : "VAL_INVALID_INPUT";
            return Error(
                httpContext,
                StatusCodes.Status400BadRequest,
                errorCode,
                "Bad request",
                "One or more validation errors occurred.",
                errors);
        }
        catch (WalletLockedException exception)
        {
            return Error(
                httpContext,
                StatusCodes.Status403Forbidden,
                "WALLET_LOCKED",
                "Forbidden",
                exception.Message);
        }
        catch (WalletNotFoundException exception)
        {
            return Error(
                httpContext,
                StatusCodes.Status404NotFound,
                "WALLET_NOT_FOUND",
                "Not found",
                exception.Message);
        }
        catch (EntityNotFoundException exception)
        {
            return Error(
                httpContext,
                StatusCodes.Status404NotFound,
                "WITHDRAWAL_NOT_FOUND",
                "Not found",
                exception.Message);
        }
        catch (InsufficientBalanceException exception)
        {
            return Error(
                httpContext,
                StatusCodes.Status400BadRequest,
                "INSUFFICIENT_BALANCE",
                "Bad request",
                exception.Message);
        }
        catch (BusinessRuleValidationException exception)
        {
            var alreadyProcessed = exception.Message.Contains("ALREADY_PROCESSED", StringComparison.OrdinalIgnoreCase);
            return Error(
                httpContext,
                alreadyProcessed ? StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict,
                alreadyProcessed ? "ALREADY_PROCESSED" : ToErrorCode(exception.Message),
                alreadyProcessed ? "Bad request" : "Conflict",
                exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Error(
                httpContext,
                StatusCodes.Status400BadRequest,
                "VAL_INVALID_INPUT",
                "Bad request",
                exception.Message);
        }
    }

    private static IResult Error(
        HttpContext httpContext,
        int status,
        string errorCode,
        string title,
        string detail,
        IDictionary<string, string[]>? errors = null)
    {
        return Results.Json(
            new ApiErrorResponse(
                "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                title,
                status,
                errorCode,
                detail,
                errors,
                GetTraceId(httpContext),
                DateTimeOffset.UtcNow),
            statusCode: status);
    }

    private static IResult VnPayResponse(string responseCode, string message)
    {
        return Results.Json(new { RspCode = responseCode, Message = message });
    }

    private static string GetTraceId(HttpContext httpContext)
    {
        return Activity.Current?.Id ?? httpContext.TraceIdentifier;
    }

    private static string ToErrorCode(string message)
    {
        var errorCode = new string(message
            .ToUpperInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());
        return string.Join('_', errorCode.Split('_', StringSplitOptions.RemoveEmptyEntries));
    }

    private static (string? UserId, IResult? Failure) GetAuthenticatedUserId(
        HttpContext context,
        params string[] allowedRoles)
    {
        var userId = context.Request.Headers["x-user-id"].FirstOrDefault();
        if (!Guid.TryParse(userId, out var parsedUserId) || parsedUserId == Guid.Empty)
            return (null, Error(
                context,
                StatusCodes.Status401Unauthorized,
                "UNAUTHORIZED",
                "Unauthorized",
                "Token is missing, expired, or invalid."));
        if (!HasRole(context, allowedRoles))
            return (null, Error(
                context,
                StatusCodes.Status403Forbidden,
                "FORBIDDEN",
                "Forbidden",
                "User does not have the required permissions."));

        return (parsedUserId.ToString(), null);
    }

    private static bool HasRole(HttpContext context, params string[] allowedRoles)
    {
        var roles = context.Request.Headers["x-user-role"]
            .SelectMany(value =>
                (value ?? string.Empty).Split(',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        return roles.Any(role => allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
    }

    private static string? GetClientIpAddress(HttpContext context)
    {
        var forwardedClientIp = context.Request.Headers["x-client-ip"].FirstOrDefault();
        var candidate = forwardedClientIp ?? context.Connection.RemoteIpAddress?.ToString();
        return IPAddress.TryParse(candidate, out var address) ? address.ToString() : null;
    }

    private static async Task<Dictionary<string, string>> ReadVnPayParametersAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            return form.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }

        using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("VNPay IPN body must be a JSON object.");

        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string ToContractTransactionType(string transactionType)
    {
        if (Enum.TryParse<WalletTransactionType>(transactionType, true, out var type))
        {
            return type switch
            {
                WalletTransactionType.TopupVnpay => "DEPOSIT",
                WalletTransactionType.WithdrawalDebit or WalletTransactionType.WithdrawalReversal => "WITHDRAWAL",
                WalletTransactionType.PurchaseDebit or WalletTransactionType.DirectGatewayPayment => "PAYMENT",
                WalletTransactionType.RefundCredit => "REFUND",
                _ => transactionType.ToUpperInvariant()
            };
        }

        return transactionType.ToUpperInvariant();
    }

    private static bool IsDebit(string transactionType)
    {
        return Enum.TryParse<WalletTransactionType>(transactionType, true, out var type) &&
               type is WalletTransactionType.PurchaseDebit or
                   WalletTransactionType.DirectGatewayPayment or
                   WalletTransactionType.WithdrawalDebit or
                   WalletTransactionType.AdjustmentDebit or
                   WalletTransactionType.TransferOut;
    }

    private sealed class WalletLockedException : Exception
    {
        public WalletLockedException() : base("Wallet account is locked or frozen.")
        {
        }
    }

    private sealed record ApiResponse<T>(
        bool Success,
        int StatusCode,
        string? Message,
        T Data,
        string TraceId,
        DateTimeOffset Timestamp);

    private sealed record ApiErrorResponse(
        string Type,
        string Title,
        int Status,
        string ErrorCode,
        string Detail,
        IDictionary<string, string[]>? Errors,
        string TraceId,
        DateTimeOffset Timestamp);
}

public sealed record VnPayTopupHttpRequest(decimal Amount, string? BankCode, string ReturnUrl);

public sealed record DirectPaymentHttpRequest(
    string OrderId,
    decimal Amount,
    string Gateway,
    string? BankCode,
    string ReturnUrl);

public sealed record WithdrawalHttpRequest(
    decimal Amount,
    string BankCode,
    string AccountNumber,
    string AccountHolderName);

public sealed record ProcessWithdrawalHttpRequest(string Action, string? AdminNote);

public sealed record BalanceAdjustmentHttpRequest(decimal Amount, string AdjustmentType, string Reason);