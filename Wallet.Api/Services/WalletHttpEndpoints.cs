namespace Wallet.Api.Services;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Net;
using Wallet.Application.Features.Commands.Topups;
using Wallet.Application.Features.Commands.WalletOperations;
using Wallet.Application.Features.Queries.GetWallet;
using Wallet.Application.Features.Queries.ListTransactions;
using Wallet.Domain.Exceptions;

public static class WalletHttpEndpoints
{
    public static IEndpointRouteBuilder MapWalletHttpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/wallets/{userId:guid}/balance", async (
            Guid userId,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var authorizationFailure = AuthorizeUser(httpContext, userId.ToString(), "BUYER", "SELLER", "ADMIN");
            if (authorizationFailure is not null)
                return authorizationFailure;

            return await Execute(async () =>
            {
                var wallet = await mediator.Send(new GetWalletQuery(userId.ToString()), cancellationToken);
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

        endpoints.MapGet("/api/v1/wallets/{userId:guid}/transactions", async (
            Guid userId,
            string? type,
            DateTimeOffset? fromDate,
            DateTimeOffset? toDate,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var authorizationFailure = AuthorizeUser(httpContext, userId.ToString(), "BUYER", "SELLER", "ADMIN");
            if (authorizationFailure is not null)
                return authorizationFailure;

            return await Execute(() => mediator.Send(
                new ListTransactionsQuery(
                    userId.ToString(),
                    page ?? 1,
                    pageSize ?? 20,
                    type,
                    fromDate,
                    toDate),
                cancellationToken));
        });

        endpoints.MapPost("/api/v1/wallet/topups", async (
            TopupHttpRequest request,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var userId = GetAuthenticatedUserId(httpContext, "BUYER", "SELLER");
            if (userId.Failure is not null)
                return userId.Failure;

            var forwardedClientIp = httpContext.Request.Headers["x-client-ip"].FirstOrDefault();
            var ipAddress = forwardedClientIp ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            if (!IPAddress.TryParse(ipAddress, out _))
                return Results.BadRequest(new { error = "A valid client IP address is required." });

            return await Execute(() => mediator.Send(
                new InitiateVnPayTopupCommand(userId.UserId!, request.Amount, ipAddress),
                cancellationToken));
        });

        endpoints.MapGet("/api/v1/wallet/vnpay/ipn", async (
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var parameters = httpContext.Request.Query.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
            var result = await mediator.Send(
                new ProcessVnPayIpnCommand(parameters, httpContext.Connection.RemoteIpAddress?.ToString()),
                cancellationToken);
            return Results.Json(new { RspCode = result.ResponseCode, Message = result.Message });
        });

        endpoints.MapPost("/api/v1/wallet/payments", async (
            DirectPaymentHttpRequest request,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var userId = GetAuthenticatedUserId(httpContext, "BUYER", "SELLER");
            if (userId.Failure is not null)
                return userId.Failure;

            return await Execute(() => mediator.Send(
                new InitiateDirectGatewayPaymentCommand(
                    userId.UserId!,
                    request.OrderId,
                    request.Amount,
                    request.Gateway),
                cancellationToken));
        });

        endpoints.MapPost("/api/v1/wallet/withdrawals", async (
            WithdrawalHttpRequest request,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var userId = GetAuthenticatedUserId(httpContext, "SELLER");
            if (userId.Failure is not null)
                return userId.Failure;

            return await Execute(() => mediator.Send(
                new RequestWithdrawalCommand(
                    userId.UserId!,
                    request.Amount,
                    request.BankName,
                    request.BankAccountNumber,
                    request.BankAccountHolder),
                cancellationToken));
        });

        endpoints.MapPost("/api/v1/admin/withdrawals/{withdrawalId:guid}", async (
            Guid withdrawalId,
            ProcessWithdrawalHttpRequest request,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var adminId = GetAuthenticatedUserId(httpContext, "ADMIN", "SUPPORT_STAFF");
            if (adminId.Failure is not null)
                return adminId.Failure;

            return await Execute(() => mediator.Send(
                new ProcessWithdrawalCommand(
                    withdrawalId.ToString(),
                    adminId.UserId!,
                    request.Action,
                    request.RejectReason),
                cancellationToken));
        });

        endpoints.MapPost("/api/v1/admin/wallets/{userId:guid}/adjustments", async (
            Guid userId,
            BalanceAdjustmentHttpRequest request,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var adminId = GetAuthenticatedUserId(httpContext, "ADMIN");
            if (adminId.Failure is not null)
                return adminId.Failure;

            return await Execute(() => mediator.Send(
                new AdjustWalletBalanceCommand(
                    userId.ToString(),
                    adminId.UserId!,
                    request.Amount,
                    request.Action,
                    request.Reason),
                cancellationToken));
        });

        return endpoints;
    }

    private static IResult? AuthorizeUser(HttpContext context, string requestedUserId, params string[] allowedRoles)
    {
        var identity = GetAuthenticatedUserId(context, allowedRoles);
        if (identity.Failure is not null)
            return identity.Failure;

        var isAdmin = HasRole(context, "ADMIN");
        return isAdmin || string.Equals(identity.UserId, requestedUserId, StringComparison.OrdinalIgnoreCase)
            ? null
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static (string? UserId, IResult? Failure) GetAuthenticatedUserId(
        HttpContext context,
        params string[] allowedRoles)
    {
        var userId = context.Request.Headers["x-user-id"].FirstOrDefault();
        if (!Guid.TryParse(userId, out _))
            return (null, Results.Unauthorized());
        if (!HasRole(context, allowedRoles))
            return (null, Results.StatusCode(StatusCodes.Status403Forbidden));

        return (userId, null);
    }

    private static bool HasRole(HttpContext context, params string[] allowedRoles)
    {
        var roles = context.Request.Headers["x-user-role"]
            .SelectMany(value =>
                (value ?? string.Empty).Split(',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        return roles.Any(role => allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(exception.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()));
        }
        catch (WalletNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (EntityNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (InsufficientBalanceException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (BusinessRuleValidationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}

public sealed record TopupHttpRequest(decimal Amount);

public sealed record DirectPaymentHttpRequest(string OrderId, decimal Amount, string Gateway);

public sealed record WithdrawalHttpRequest(
    decimal Amount,
    string BankName,
    string BankAccountNumber,
    string BankAccountHolder);

public sealed record ProcessWithdrawalHttpRequest(string Action, string? RejectReason);

public sealed record BalanceAdjustmentHttpRequest(decimal Amount, string Action, string Reason);