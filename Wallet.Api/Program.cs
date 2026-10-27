using Microsoft.EntityFrameworkCore;
using Wallet.Api.Extensions;
using Wallet.Api.Middleware;
using Wallet.Application;
using Wallet.Infrastructure;
using Wallet.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// 1. OpenTelemetry Distributed Tracing (Grafana Tempo)
builder.Services.AddServiceObservability(builder.Configuration, "Wallet.Api");

// 2. Application & Infrastructure Layers
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// 3. REST MVC Controllers
builder.Services.AddControllers();

// 4. Swagger / OpenAPI & JSON Serialization
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new Wallet.Api.Common.Converters.PascalCaseJsonStringEnumConverter(allowIntegerValues: false));
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new Wallet.Api.Common.Converters.PascalCaseJsonStringEnumConverter(allowIntegerValues: false));
});

var app = builder.Build();

app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

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

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Wallet.Api" }));

app.Run();
