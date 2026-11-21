using Microsoft.EntityFrameworkCore;
using Wallet.Api.Common;
using Wallet.Api;
using Wallet.Api.Extensions;
using Wallet.Application;
using Wallet.Infrastructure;
using Wallet.Infrastructure.Persistence;
using Wallet.Infrastructure.Persistence.Seeds;

var builder = WebApplication.CreateBuilder(args);

// 1. Auxiliary Service Defaults (Observability, Swagger, JSON options)
builder.Services.AddServiceDefaults(builder.Configuration, "wallet-service");

// 2. Application & Infrastructure Dependency Injection
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddHostedService<StripeTopupReconciliationWorker>();
builder.Services.AddHostedService<WithdrawalNotificationRetryWorker>();
builder.Services.AddHostedService<JournalPostingWorker>();

// 3. REST Services
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create;
    });

var app = builder.Build();
Wallet.Infrastructure.Payments.StripeSettings.EnsureValid(app.Configuration, app.Logger);

// 4. Default Middleware Pipeline (Exception Handler, Swagger UI)
app.UseServiceDefaults();

// Automatically apply database migrations on startup if relational DB is available
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
    try
    {
        if (db.Database.IsRelational())
        {
            db.Database.Migrate();
        }

        if (app.Environment.IsDevelopment())
        {
            WalletSeedData.Seed(db);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Database migration or development seeding deferred on startup: {Message}", ex.Message);
    }
}

app.MapControllers();
// Readiness, not just "the process is up": the database must answer, otherwise 503 so the central health view can tell.
app.MapGet("/health", async (Wallet.Infrastructure.Persistence.WalletDbContext db, CancellationToken ct) =>
{
    try
    {
        return await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "Healthy", service = "wallet-service" })
            : Results.Json(new { status = "Unhealthy", service = "wallet-service", message = "The database is not reachable." }, statusCode: 503);
    }
    catch (Exception)
    {
        return Results.Json(new { status = "Unhealthy", service = "wallet-service", message = "The database check failed." }, statusCode: 503);
    }
});

app.Run();
