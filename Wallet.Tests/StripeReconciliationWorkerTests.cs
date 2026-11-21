namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Api;
using Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;
using Xunit;

public sealed class StripeReconciliationWorkerTests
{
    [Fact]
    public async Task Worker_WhenStripeUnconfigured_ExitsWithoutSendingCommand()
    {
        var sender = new RecordingReconcileSender();
        using var provider = BuildScopeProvider(sender);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "",
            ["Stripe:WebhookSecret"] = ""
        }).Build();

        using var worker = new StripeTopupReconciliationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            NullLogger<StripeTopupReconciliationWorker>.Instance,
            TimeSpan.FromMilliseconds(10));

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        Assert.NotNull(worker.ExecuteTask);
        await worker.ExecuteTask!;
        Assert.Equal(0, sender.Calls);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Worker_WhenStripeIncomplete_ExitsGracefullyWithoutThrowing()
    {
        var sender = new RecordingReconcileSender();
        using var provider = BuildScopeProvider(sender);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_123",
            ["Stripe:WebhookSecret"] = ""
        }).Build();

        using var worker = new StripeTopupReconciliationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            NullLogger<StripeTopupReconciliationWorker>.Instance,
            TimeSpan.FromMilliseconds(10));

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        Assert.NotNull(worker.ExecuteTask);
        await worker.ExecuteTask!;
        Assert.Equal(0, sender.Calls);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Worker_WhenConfigured_DispatchesReconcileCommandAndSurvivesTransientException()
    {
        var secondCallCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingReconcileSender(throwOnCall: 1, onCompletedCall: call =>
        {
            if (call >= 2)
            {
                secondCallCompleted.TrySetResult();
            }
        });

        using var provider = BuildScopeProvider(sender);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Stripe:SecretKey"] = "sk_test_not-a-real-key",
            ["Stripe:WebhookSecret"] = "whsec_not-a-real-secret",
            ["Stripe:SuccessUrl"] = "https://example.test/success",
            ["Stripe:CancelUrl"] = "https://example.test/cancel"
        }).Build();

        using var worker = new StripeTopupReconciliationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            NullLogger<StripeTopupReconciliationWorker>.Instance,
            TimeSpan.FromMilliseconds(15));

        await worker.StartAsync(CancellationToken.None);
        await secondCallCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(sender.Calls >= 2);
        Assert.True(sender.SawReconcileCommand);
    }

    private static ServiceProvider BuildScopeProvider(ISender sender) =>
        new ServiceCollection().AddScoped(_ => sender).BuildServiceProvider();

    private sealed class RecordingReconcileSender(
        int? throwOnCall = null,
        Action<int>? onCompletedCall = null) : ISender
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public bool SawReconcileCommand { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (request is ReconcilePendingTopupsCommand)
            {
                SawReconcileCommand = true;
            }

            if (throwOnCall == call)
            {
                throw new InvalidOperationException("Simulated transient provider error");
            }

            onCompletedCall?.Invoke(call);
            object boxed = 1;
            return Task.FromResult((TResponse)boxed);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
