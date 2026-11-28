namespace Wallet.Api.Swagger;

using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

public sealed class StripeHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath;
        var header = path switch
        {
            "api/v1/wallets/stripe/webhooks" => "Stripe-Signature",
            "api/v1/wallets/topups/stripe" => "Idempotency-Key",
            _ => null
        };
        if (header is null || operation.Parameters is null)
            return;

        foreach (var parameter in operation.Parameters)
        {
            if (parameter.In == ParameterLocation.Header &&
                string.Equals(parameter.Name, header, StringComparison.OrdinalIgnoreCase))
                parameter.Required = true;
        }
    }
}
