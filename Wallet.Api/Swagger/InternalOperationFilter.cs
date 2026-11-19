namespace Wallet.Api.Swagger;

using System;
using System.Collections.Generic;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

public sealed class InternalOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.RelativePath is not { } path ||
            !path.StartsWith("api/v1/internal", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Internal-Service-Key",
            In = ParameterLocation.Header,
            Required = true,
            Description = "Internal service secret key.",
            Schema = new OpenApiSchema { Type = "string" }
        });
    }
}
