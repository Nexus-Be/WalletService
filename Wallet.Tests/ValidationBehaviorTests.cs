namespace Wallet.Tests;

using System;
using System.Threading.Tasks;
using MediatR;
using Wallet.Application.Common.Behaviors;
using Wallet.Application.Common.Exceptions;
using Wallet.Application.Features.Transfers.Commands.Transfer;
using Xunit;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_AggregatesFieldErrors_WithoutCallingHandler()
    {
        var request = new TransferCommand(string.Empty, 0m);
        var handlerCalled = false;
        RequestHandlerDelegate<TransferResponse> next = () =>
        {
            handlerCalled = true;
            return Task.FromResult(new TransferResponse(
                Guid.NewGuid().ToString(),
                0m,
                "Vnd",
                "Success",
                DateTimeOffset.UtcNow));
        };
        var behavior = new ValidationBehavior<TransferCommand, TransferResponse>();

        var exception =
            await Assert.ThrowsAsync<RequestValidationException>(() =>
                behavior.Handle(request, next, CancellationToken.None));

        Assert.False(handlerCalled);
        Assert.Equal("VAL_INVALID_INPUT", exception.ErrorCode);
        Assert.Contains(nameof(request.RecipientUserId), exception.Errors.Keys);
        Assert.Contains(nameof(request.Amount), exception.Errors.Keys);
        Assert.Contains(nameof(request.SenderUserId), exception.Errors.Keys);
        Assert.All(exception.Errors.Values, messages => Assert.NotEmpty(messages));
    }
}