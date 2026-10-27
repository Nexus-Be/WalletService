namespace Wallet.Api.Services;

using System.Globalization;
using System.Net;
using FluentValidation;
using Grpc.Core;
using MediatR;
using ShoppingCart.Contracts.V1;
using global::Wallet.Application.DTOs;
using global::Wallet.Application.Features.Commands.Topups;
using global::Wallet.Application.Features.Commands.Transfer;
using global::Wallet.Application.Features.Queries.GetWallet;
using global::Wallet.Application.Features.Queries.ListTransactions;
using global::Wallet.Domain.Exceptions;

public class WalletGrpcService : WalletApi.WalletApiBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<WalletGrpcService> _logger;

    public WalletGrpcService(IMediator mediator, ILogger<WalletGrpcService> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public override async Task<ShoppingCart.Contracts.V1.Wallet> GetWallet(UserRequest request,
        ServerCallContext context)
    {
        var callerUserId = GetAuthenticatedUserId(context, "BUYER", "SELLER", "ADMIN");
        var effectiveUserId = HasRole(context, "ADMIN") ? request.UserId : callerUserId;
        if (!Guid.TryParse(effectiveUserId, out _) || string.IsNullOrWhiteSpace(effectiveUserId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "UserId is invalid."));

        WalletDto result;
        try
        {
            result = await _mediator.Send(new GetWalletQuery(effectiveUserId), context.CancellationToken);
        }
        catch (WalletNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        return new ShoppingCart.Contracts.V1.Wallet
        {
            Id = result.Id,
            UserId = result.UserId,
            Balance = result.Balance.ToString("0.00", CultureInfo.InvariantCulture),
            Currency = result.Currency,
            Status = result.Status,
            UpdatedAt = result.UpdatedAt.ToString("o")
        };
    }

    public override async Task<WalletTransaction> Deposit(WalletDepositRequest request, ServerCallContext context)
    {
        var effectiveUserId = GetAuthenticatedUserId(context, "BUYER", "SELLER");

        if (!decimal.TryParse(request.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Deposit amount is invalid."));
        if (!string.IsNullOrWhiteSpace(request.Currency) &&
            !string.Equals(request.Currency, "VND", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Wallet top-ups are only supported in VND."));
        if (!string.Equals(request.PaymentMethod, "VNPAY", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Deposits must be made through VNPay."));

        try
        {
            var result = await _mediator.Send(
                new InitiateVnPayTopupCommand(
                    effectiveUserId,
                    amount,
                    GetClientIpAddress(context)),
                context.CancellationToken);
            return new WalletTransaction
            {
                Id = result.TopupId,
                WalletId = result.WalletId,
                UserId = result.UserId,
                Type = "TopupVnpay",
                Amount = result.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                Currency = "VND",
                BalanceAfter = result.Balance.ToString("0.00", CultureInfo.InvariantCulture),
                Status = "PENDING",
                Note = result.PaymentUrl,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o")
            };
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (WalletNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error processing deposit for user {UserId}", effectiveUserId);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    public override Task<WalletTransaction> Withdraw(WalletWithdrawRequest request, ServerCallContext context)
    {
        throw new RpcException(new Status(
            StatusCode.Unimplemented,
            "Withdrawals require complete bank details and approval; use POST /api/v1/wallet/withdrawals."));
    }

    public override async Task<WalletTransaction> Transfer(WalletTransferRequest request, ServerCallContext context)
    {
        var effectiveSenderId = GetAuthenticatedUserId(context, "BUYER", "SELLER");

        if (!decimal.TryParse(request.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Transfer amount is invalid."));

        try
        {
            var result =
                await _mediator.Send(
                    new TransferCommand(effectiveSenderId, request.RecipientUserId, amount, request.Note),
                    context.CancellationToken);

            return MapTransaction(result);
        }
        catch (InsufficientBalanceException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (WalletNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (BusinessRuleValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    public override async Task<WalletTransactionPage> ListTransactions(WalletTransactionsRequest request,
        ServerCallContext context)
    {
        var callerUserId = GetAuthenticatedUserId(context, "BUYER", "SELLER", "ADMIN");
        var effectiveUserId = HasRole(context, "ADMIN") ? request.UserId : callerUserId;
        if (!Guid.TryParse(effectiveUserId, out _) || string.IsNullOrWhiteSpace(effectiveUserId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "UserId is invalid."));

        WalletTransactionPageDto result;
        try
        {
            result = await _mediator.Send(
                new ListTransactionsQuery(effectiveUserId, request.Page, request.PageSize, request.Type),
                context.CancellationToken);
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        var page = new WalletTransactionPage
        {
            Total = result.Total
        };

        foreach (var item in result.Items)
        {
            page.Items.Add(MapTransaction(item));
        }

        return page;
    }

    private static WalletTransaction MapTransaction(WalletTransactionDto dto)
    {
        return new WalletTransaction
        {
            Id = dto.Id,
            WalletId = dto.WalletId,
            UserId = dto.UserId,
            Type = dto.Type,
            Amount = dto.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            Currency = dto.Currency,
            BalanceAfter = dto.BalanceAfter.ToString("0.00", CultureInfo.InvariantCulture),
            Status = dto.Status,
            Note = dto.Note,
            CreatedAt = dto.CreatedAt.ToString("o")
        };
    }

    private static string GetClientIpAddress(ServerCallContext context)
    {
        var forwardedClientIp = context.RequestHeaders.Get("x-client-ip")?.Value;
        if (!string.IsNullOrWhiteSpace(forwardedClientIp))
        {
            if (!IPAddress.TryParse(forwardedClientIp, out _))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "The request IP address is invalid."));
            return forwardedClientIp;
        }

        var peer = context.Peer;
        var separatorIndex = peer.IndexOf(':');
        if (separatorIndex < 0)
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "Unable to determine the request IP address."));

        var endpoint = peer[(separatorIndex + 1)..];
        string address;
        if (endpoint.StartsWith("[", StringComparison.Ordinal))
        {
            var closingBracketIndex = endpoint.IndexOf(']');
            if (closingBracketIndex <= 1)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "The request IP address is invalid."));
            address = endpoint[1..closingBracketIndex];
        }
        else
        {
            var portSeparator = endpoint.LastIndexOf(':');
            if (portSeparator <= 0)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "The request IP address is invalid."));
            address = endpoint[..portSeparator];
        }

        if (!IPAddress.TryParse(address, out var ipAddress))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The request IP address is invalid."));

        return ipAddress.ToString();
    }

    private static string GetAuthenticatedUserId(ServerCallContext context, params string[] allowedRoles)
    {
        var userId = context.RequestHeaders.Get("x-user-id")?.Value;
        if (!Guid.TryParse(userId, out var parsedUserId) || parsedUserId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid x-user-id metadata."));
        if (!HasRole(context, allowedRoles))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "The caller does not have the required role."));

        return parsedUserId.ToString();
    }

    private static bool HasRole(ServerCallContext context, params string[] allowedRoles)
    {
        var roles = context.RequestHeaders.Get("x-user-role")?.Value ?? string.Empty;
        return roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(role => allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
    }
}