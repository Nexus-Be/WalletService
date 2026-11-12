namespace Wallet.Api.Swagger;

using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

public sealed class AnonymousOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute), inherit: true) ||
            context.MethodInfo.DeclaringType?.IsDefined(typeof(AllowAnonymousAttribute), inherit: true) == true)
            operation.Security = [new OpenApiSecurityRequirement()];
    }
}
