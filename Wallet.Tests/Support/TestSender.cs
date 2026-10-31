namespace Wallet.Tests.Support;

using MediatR;
using Wallet.Application.Features.Wallets.Commands.EnsureWallet;
using Wallet.Application.Interfaces;

/// <summary>Minimal in-process ISender that routes EnsureWalletCommand to its real handler.</summary>
public sealed class TestSender : ISender
{
    private readonly IWalletDbContext _context;

    public TestSender(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is EnsureWalletCommand ensureWallet)
        {
            var response = await new EnsureWalletCommandHandler(_context).Handle(ensureWallet, cancellationToken);
            return (TResponse)(object)response!;
        }

        throw new NotSupportedException($"TestSender does not support '{request.GetType().Name}'.");
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        throw new NotSupportedException($"TestSender does not support '{typeof(TRequest).Name}'.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"TestSender does not support '{request.GetType().Name}'.");
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"TestSender does not support streaming '{request.GetType().Name}'.");
    }

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"TestSender does not support streaming '{request.GetType().Name}'.");
    }
}
