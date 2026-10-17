using Wallet.Api.Extensions;
using Wallet.Api.Services;
using Wallet.Application;
using Wallet.Infrastructure;

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

// 4. Swagger / OpenAPI for REST endpoints (if any)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 5. Map gRPC Services
app.MapGrpcService<WalletGrpcService>();

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. WalletService is running.");

app.Run();
