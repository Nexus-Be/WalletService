using System.Globalization;
using Grpc.Core;
using MediatR;
using ShoppingCart.Contracts.V1;
using Wallet.Application.DTOs;
using Wallet.Application.Features.Commands.Deposit;
using Wallet.Application.Features.Commands.Transfer;
using Wallet.Application.Features.Commands.Withdraw;
using Wallet.Application.Features.Queries.GetWallet;
using Wallet.Application.Features.Queries.ListTransactions;
using Wallet.Domain.Exceptions;

namespace Wallet.Api.Services;

public class WalletGrpcService : WalletApi.WalletApiBase
{
    private readonly ILogger<WalletGrpcService> _logger;
    private readonly IMediator _mediator;

    public WalletGrpcService(IMediator mediator, ILogger<WalletGrpcService> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public override async Task<ShoppingCart.Contracts.V1.Wallet> GetWallet(UserRequest request,
        ServerCallContext context)
    {
        var effectiveUserId = context.RequestHeaders.Get("x-user-id")?.Value ?? request.UserId;

        if (string.IsNullOrWhiteSpace(effectiveUserId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "UserId không được để trống."));

        var result = await _mediator.Send(new GetWalletQuery(effectiveUserId), context.CancellationToken);

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
        var effectiveUserId = context.RequestHeaders.Get("x-user-id")?.Value ?? request.UserId;

        if (!decimal.TryParse(request.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Số tiền nạp không hợp lệ."));

        try
        {
            var result =
                await _mediator.Send(
                    new DepositCommand(effectiveUserId, amount, request.Currency, request.PaymentMethod),
                    context.CancellationToken);

            return MapTransaction(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xử lý nạp tiền cho user {UserId}", effectiveUserId);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    public override async Task<WalletTransaction> Withdraw(WalletWithdrawRequest request, ServerCallContext context)
    {
        var effectiveUserId = context.RequestHeaders.Get("x-user-id")?.Value ?? request.UserId;

        if (!decimal.TryParse(request.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Số tiền rút không hợp lệ."));

        try
        {
            var result =
                await _mediator.Send(
                    new WithdrawCommand(effectiveUserId, amount, request.Currency, request.BankAccount),
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi rút tiền cho user {UserId}", effectiveUserId);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    public override async Task<WalletTransaction> Transfer(WalletTransferRequest request, ServerCallContext context)
    {
        var effectiveSenderId = context.RequestHeaders.Get("x-user-id")?.Value ?? request.SenderUserId;

        if (!decimal.TryParse(request.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Số tiền chuyển không hợp lệ."));

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi chuyển tiền từ user {Sender} sang user {Recipient}", effectiveSenderId,
                request.RecipientUserId);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    public override async Task<WalletTransactionPage> ListTransactions(WalletTransactionsRequest request,
        ServerCallContext context)
    {
        var effectiveUserId = context.RequestHeaders.Get("x-user-id")?.Value ?? request.UserId;

        var result = await _mediator.Send(
            new ListTransactionsQuery(effectiveUserId, request.Page, request.PageSize, request.Type),
            context.CancellationToken);

        var page = new WalletTransactionPage
        {
            Total = result.Total
        };

        foreach (var item in result.Items) page.Items.Add(MapTransaction(item));

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
}