namespace Wallet.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Api.Common;
using Wallet.Api.Middleware;
using Wallet.Application.Common.Exceptions;
using Xunit;

public sealed class ApiValidationProblemFactoryTests
{
    [Fact]
    public void Create_Returns_Problem_With_Validation_Error_Code()
    {
        var actionContext = CreateActionContext();
        actionContext.ModelState.AddModelError("Amount", "The Amount field is required.");

        var result = ApiValidationProblemFactory.Create(actionContext);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.Contains("application/problem+json", objectResult.ContentTypes);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(objectResult.Value));
        var root = document.RootElement;
        Assert.Equal("VAL_INVALID_INPUT", root.GetProperty("errorCode").GetString());
        Assert.Equal(StatusCodes.Status400BadRequest, root.GetProperty("status").GetInt32());
        Assert.Contains("The Amount field is required.", root.GetProperty("detail").GetString());
        Assert.True(root.TryGetProperty("timestamp", out _));
        Assert.True(root.TryGetProperty("errors", out var errorsElement));
        Assert.Equal("The Amount field is required.", errorsElement.GetProperty("Amount")[0].GetString());
    }

    [Fact]
    public void Create_Hides_Internal_Type_Names_From_Json_Binding_Errors()
    {
        var actionContext = CreateActionContext();
        actionContext.ModelState.AddModelError(
            "$",
            "The JSON value could not be converted to Wallet.Tests.Internal.Request. Path: $ | LineNumber: 0 | BytePositionInLine: 1.");
        actionContext.ModelState.AddModelError("request", "The request field is required.");

        var result = ApiValidationProblemFactory.Create(actionContext);

        var objectResult = Assert.IsType<ObjectResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(objectResult.Value));
        var detail = document.RootElement.GetProperty("detail").GetString();
        Assert.Equal(ApiValidationProblemFactory.UnreadableBodyDetail, detail);
        Assert.DoesNotContain("Wallet.Tests", detail);
        Assert.DoesNotContain("LineNumber", detail);
    }

    [Fact]
    public async Task ModelBindingAndPipelineValidation_UseTheSameProblemDetailsShape()
    {
        var modelState = CreateActionContext();
        modelState.ModelState.AddModelError("Amount", "Amount must be greater than zero.");
        var mvcResult = Assert.IsType<ObjectResult>(ApiValidationProblemFactory.Create(modelState));
        var mvcJson = JsonSerializer.Serialize(mvcResult.Value);

        var errors = new Dictionary<string, string[]>
        {
            ["Amount"] = ["Amount must be greater than zero."]
        };
        var validationException = new RequestValidationException(
            "Amount must be greater than zero.",
            errors);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "validation-test-trace";
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            _ => Task.FromException(validationException),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        var middlewareJson = await new StreamReader(context.Response.Body).ReadToEndAsync();

        using var mvcDocument = JsonDocument.Parse(mvcJson);
        using var middlewareDocument = JsonDocument.Parse(middlewareJson);
        var mvcRoot = mvcDocument.RootElement;
        var middlewareRoot = middlewareDocument.RootElement;
        var expectedFields = new[]
        {
            "type", "title", "status", "errorCode", "detail", "errors", "traceId", "timestamp"
        };

        Assert.Contains("application/problem+json", mvcResult.ContentTypes);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(
            expectedFields.OrderBy(field => field),
            mvcRoot.EnumerateObject().Select(property => property.Name).OrderBy(field => field));
        Assert.Equal(
            expectedFields.OrderBy(field => field),
            middlewareRoot.EnumerateObject().Select(property => property.Name).OrderBy(field => field));
        Assert.Equal(mvcRoot.GetProperty("type").GetString(), middlewareRoot.GetProperty("type").GetString());
        Assert.Equal(mvcRoot.GetProperty("title").GetString(), middlewareRoot.GetProperty("title").GetString());
        Assert.Equal(mvcRoot.GetProperty("status").GetInt32(), middlewareRoot.GetProperty("status").GetInt32());
        Assert.Equal(mvcRoot.GetProperty("errorCode").GetString(), middlewareRoot.GetProperty("errorCode").GetString());
        Assert.Equal(mvcRoot.GetProperty("detail").GetString(), middlewareRoot.GetProperty("detail").GetString());
        Assert.Equal(
            mvcRoot.GetProperty("errors").GetProperty("Amount")[0].GetString(),
            middlewareRoot.GetProperty("errors").GetProperty("Amount")[0].GetString());
    }

    private static ActionContext CreateActionContext() =>
        new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
}