namespace Wallet.Api.Extensions;

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddServiceObservability(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? configuration["Otel:Endpoint"] ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation();

                if (Uri.TryCreate(otlpEndpoint?.Trim(), UriKind.Absolute, out var otlpUri))
                {
                    t.AddOtlpExporter(o => o.Endpoint = otlpUri);
                }
            });

        return services;
    }
}
