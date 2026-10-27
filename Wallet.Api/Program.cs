using Microsoft.EntityFrameworkCore;
using Wallet.Api.Extensions;
using Wallet.Api.Middleware;
using Wallet.Api.Services;
using Wallet.Application;
using Wallet.Infrastructure;
using Wallet.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// 1. OpenTelemetry Distributed Tracing (Grafana Tempo)
builder.Services.AddServiceObservability(builder.Configuration, "Wallet.Api");

// 2. Application & Infrastructure Layers
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// 3. gRPC Server
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});
builder.Services.AddGrpcReflection();

// 4. Swagger / OpenAPI for REST endpoints (if any)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGrpcReflectionService();
}

app.UseHttpsRedirection();

// 5. Map gRPC Services
app.MapGrpcService<WalletGrpcService>();
app.MapWalletHttpEndpoints();

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
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Database migration deferred on startup: {Message}", ex.Message);
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Wallet.Api" }));
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. WalletService is running.");

app.Run();