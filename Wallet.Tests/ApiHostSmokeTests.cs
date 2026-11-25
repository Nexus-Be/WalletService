namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Infrastructure.Persistence;
using Xunit;

public sealed class ApiHostSmokeTests
{
    [Fact]
    public async Task HealthEndpoint_Returns200Healthy()
    {
        await using var factory = new WalletApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("wallet-service", document.RootElement.GetProperty("service").GetString());
    }

    [Fact]
    public async Task SwaggerJson_Returns200ValidOpenApiDocument()
    {
        await using var factory = new WalletApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var openApiVersion = root.GetProperty("openapi").GetString();
        Assert.NotNull(openApiVersion);
        Assert.StartsWith("3.", openApiVersion, StringComparison.Ordinal);

        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/wallets/me", out _));
        Assert.True(paths.TryGetProperty("/api/v1/wallets/transactions", out _));
        Assert.True(paths.TryGetProperty("/api/v1/admin/wallets/ledger", out _));
        Assert.True(paths.TryGetProperty("/api/v1/admin/wallets/reconciliation", out _));

        foreach (var path in paths.EnumerateObject())
        {
            Assert.DoesNotContain("/internal/", path.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task UserAndAdminReadEndpoints_BindQueryRecordsAndHeadersThroughMvcPipeline()
    {
        await using var factory = new WalletApiFactory();
        using var client = factory.CreateClient();
        var buyerId = Guid.NewGuid().ToString("D");
        var adminId = Guid.NewGuid().ToString("D");

        using (var walletReq = NewRequest(HttpMethod.Get, "/api/v1/wallets/me", buyerId, "buyer"))
        using (var walletRes = await client.SendAsync(walletReq))
        {
            Assert.Equal(HttpStatusCode.OK, walletRes.StatusCode);
            using var walletJson = JsonDocument.Parse(await walletRes.Content.ReadAsStringAsync());
            Assert.Equal(buyerId, walletJson.RootElement.GetProperty("userId").GetString());
            Assert.Equal("Active", walletJson.RootElement.GetProperty("status").GetString());
        }

        using (var txReq = NewRequest(HttpMethod.Get, "/api/v1/wallets/transactions?page=1&pageSize=10&type=ALL", buyerId, "buyer"))
        using (var txRes = await client.SendAsync(txReq))
        {
            Assert.Equal(HttpStatusCode.OK, txRes.StatusCode);
            using var txJson = JsonDocument.Parse(await txRes.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, txJson.RootElement.GetProperty("items").ValueKind);
            Assert.Equal(1, txJson.RootElement.GetProperty("pagination").GetProperty("pageIndex").GetInt32());
        }

        using (var ledgerReq = NewRequest(HttpMethod.Get, "/api/v1/admin/wallets/ledger?page=1&pageSize=10", adminId, "admin"))
        using (var ledgerRes = await client.SendAsync(ledgerReq))
        {
            Assert.Equal(HttpStatusCode.OK, ledgerRes.StatusCode);
            using var ledgerJson = JsonDocument.Parse(await ledgerRes.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, ledgerJson.RootElement.GetProperty("items").ValueKind);
        }

        using (var reconReq = NewRequest(HttpMethod.Get, "/api/v1/admin/wallets/reconciliation?page=1&pageSize=10", adminId, "admin"))
        using (var reconRes = await client.SendAsync(reconReq))
        {
            Assert.Equal(HttpStatusCode.OK, reconRes.StatusCode);
            using var reconJson = JsonDocument.Parse(await reconRes.Content.ReadAsStringAsync());
            Assert.Equal(1, reconJson.RootElement.GetProperty("pagination").GetProperty("totalCount").GetInt32());
        }

        using (var invalidPageReq = NewRequest(HttpMethod.Get, "/api/v1/admin/wallets/ledger?page=0", adminId, "admin"))
        using (var invalidPageRes = await client.SendAsync(invalidPageReq))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidPageRes.StatusCode);
            using var errorJson = JsonDocument.Parse(await invalidPageRes.Content.ReadAsStringAsync());
            Assert.Equal("VAL_INVALID_INPUT", errorJson.RootElement.GetProperty("errorCode").GetString());
        }

        using (var staffReconReq = NewRequest(HttpMethod.Get, "/api/v1/admin/wallets/reconciliation?page=1&pageSize=10", adminId, "staff"))
        using (var staffReconRes = await client.SendAsync(staffReconReq))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staffReconRes.StatusCode);
        }
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri, string userId, string roles)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("x-user-id", userId);
        request.Headers.Add("x-user-roles", roles);
        return request;
    }

    private sealed class WalletApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = $"wallet-smoke-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=localhost;Port=5440;Database=wallet_smoke_unused;Username=postgres;Password=unused");
            builder.UseSetting("Swagger:Enabled", "true");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Swagger:Enabled"] = "true",
                    ["Stripe:SecretKey"] = "",
                    ["Stripe:WebhookSecret"] = "",
                    ["Stripe:SuccessUrl"] = "",
                    ["Stripe:CancelUrl"] = "",
                    ["Otel:Endpoint"] = "",
                    ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Port=5440;Database=wallet_smoke_unused;Username=postgres;Password=unused"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                var toRemove = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(DbContextOptions<WalletDbContext>) ||
                        descriptor.ServiceType == typeof(DbContextOptions) ||
                        descriptor.ServiceType == typeof(WalletDbContext) ||
                        descriptor.ServiceType == typeof(IWalletDbContext) ||
                        (descriptor.ServiceType.IsGenericType &&
                         descriptor.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)))
                    .ToList();

                foreach (var descriptor in toRemove)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<WalletDbContext>(options =>
                    options.UseInMemoryDatabase(_dbName)
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

                services.AddScoped<IWalletDbContext>(sp =>
                    new InMemoryIWalletDbContextAdapter(sp.GetRequiredService<WalletDbContext>()));
            });
        }
    }

    private sealed class InMemoryIWalletDbContextAdapter(WalletDbContext inner) : IWalletDbContext
    {
        public DbSet<WalletAccount> Wallets => inner.Wallets;
        public DbSet<AuctionHold> AuctionHolds => inner.AuctionHolds;
        public DbSet<AuctionHoldOperation> AuctionHoldOperations => inner.AuctionHoldOperations;
        public DbSet<AuctionHoldCapture> AuctionHoldCaptures => inner.AuctionHoldCaptures;
        public DbSet<AuctionCaptureTransfer> AuctionCaptureTransfers => inner.AuctionCaptureTransfers;
        public DbSet<AuctionDepositEscrow> AuctionDepositEscrows => inner.AuctionDepositEscrows;
        public DbSet<OrderEscrow> OrderEscrows => inner.OrderEscrows;
        public DbSet<WalletTransaction> WalletTransactions => inner.WalletTransactions;
        public DbSet<BankAccount> BankAccounts => inner.BankAccounts;
        public DbSet<WalletTopup> WalletTopups => inner.WalletTopups;
        public DbSet<DirectPaymentOrder> DirectPaymentOrders => inner.DirectPaymentOrders;
        public DbSet<StripePaymentAttempt> StripePaymentAttempts => inner.StripePaymentAttempts;
        public DbSet<StripeWebhookEvent> StripeWebhookEvents => inner.StripeWebhookEvents;
        public DbSet<VnpayIpnLog> VnpayIpnLogs => inner.VnpayIpnLogs;
        public DbSet<WithdrawalRequest> WithdrawalRequests => inner.WithdrawalRequests;
        public DbSet<AuditLog> AuditLogs => inner.AuditLogs;
        public DbSet<LedgerSettings> LedgerSettings => inner.LedgerSettings;
        public DbSet<LedgerPostingPending> LedgerPostingPending => inner.LedgerPostingPending;
        public DbSet<LedgerAccount> LedgerAccounts => inner.LedgerAccounts;
        public DbSet<JournalEntry> JournalEntries => inner.JournalEntries;
        public DbSet<JournalLine> JournalLines => inner.JournalLines;
        public DbSet<ReconciliationRun> ReconciliationRuns => inner.ReconciliationRuns;
        public DbSet<ReconciliationFinding> ReconciliationFindings => inner.ReconciliationFindings;
        public DbSet<OrderSettlement> OrderSettlements => inner.OrderSettlements;
        public DbSet<PlatformSubsidyBudget> PlatformSubsidyBudgets => inner.PlatformSubsidyBudgets;
        public DbSet<PlatformSubsidyReservation> PlatformSubsidyReservations => inner.PlatformSubsidyReservations;
        public DbSet<RefundFundPayout> RefundFundPayouts => inner.RefundFundPayouts;
        public DatabaseFacade Database => inner.Database;

        public Task LockOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LockAuctionKeyAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AuctionHold?> LockAuctionHoldAsync(Guid holdId, CancellationToken cancellationToken) =>
            inner.AuctionHolds.SingleOrDefaultAsync(hold => hold.Id == holdId, cancellationToken);

        public Task<WalletAccount?> LockWalletByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.Wallets.SingleOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        public Task<WalletAccount?> LockWalletByIdAsync(Guid walletId, CancellationToken cancellationToken) =>
            inner.Wallets.SingleOrDefaultAsync(w => w.Id == walletId, cancellationToken);

        public Task<WalletTopup?> LockTopupByIdAsync(Guid topupId, CancellationToken cancellationToken) =>
            inner.WalletTopups.SingleOrDefaultAsync(t => t.Id == topupId, cancellationToken);

        public Task<DirectPaymentOrder?> LockDirectPaymentOrderByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            inner.DirectPaymentOrders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        public Task<StripePaymentAttempt?> LockStripePaymentAttemptAsync(Guid attemptId, CancellationToken cancellationToken) =>
            inner.StripePaymentAttempts.SingleOrDefaultAsync(a => a.Id == attemptId, cancellationToken);

        public Task<WithdrawalRequest?> LockWithdrawalRequestAsync(Guid withdrawalId, CancellationToken cancellationToken) =>
            inner.WithdrawalRequests.SingleOrDefaultAsync(r => r.Id == withdrawalId, cancellationToken);

        public Task<OrderEscrow?> LockOrderEscrowByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            inner.OrderEscrows.SingleOrDefaultAsync(e => e.OrderId == orderId, cancellationToken);

        public async Task<IReadOnlyList<AuctionDepositEscrow>> LockDepositEscrowsByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            (await inner.AuctionDepositEscrows.Where(e => e.OrderId == orderId).ToListAsync(cancellationToken))
                .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToList();

        public Task<AuctionDepositEscrow?> LockDepositEscrowByHoldIdAsync(Guid holdId, CancellationToken cancellationToken) =>
            inner.AuctionDepositEscrows.SingleOrDefaultAsync(e => e.HoldId == holdId, cancellationToken);

        public Task<PlatformSubsidyBudget?> LockSubsidyBudgetAsync(Guid budgetId, CancellationToken cancellationToken) =>
            inner.PlatformSubsidyBudgets.SingleOrDefaultAsync(b => b.Id == budgetId, cancellationToken);

        public void EnsureNoPendingChanges() => inner.EnsureNoPendingChanges();

        public Task ReloadUnmodifiedAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class =>
            inner.ReloadUnmodifiedAsync(entity, cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);
    }
}
