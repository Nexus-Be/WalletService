namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Wallet.Api.Filters;
using Xunit;

public sealed class RequireRoleAttributeTests
{
    [Fact]
    public void AdminOnlyRequirement_DeniesStaff()
    {
        var context = CreateContext("staff");
        var filterContext = CreateFilterContext(context);

        new RequireRoleAttribute("admin").OnAuthorization(filterContext);

        var result = Assert.IsType<ObjectResult>(filterContext.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public void AdminOnlyRequirement_AllowsAdmin()
    {
        var context = CreateContext("admin");
        var filterContext = CreateFilterContext(context);

        new RequireRoleAttribute("admin").OnAuthorization(filterContext);

        Assert.Null(filterContext.Result);
    }

    [Fact]
    public void RoleRequirement_ReturnsUnauthorizedWithoutIdentity()
    {
        var filterContext = CreateFilterContext(new DefaultHttpContext());

        new RequireRoleAttribute("admin").OnAuthorization(filterContext);

        var result = Assert.IsType<ObjectResult>(filterContext.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public void AdminOrStaffRequirement_AllowsStaff()
    {
        var filterContext = CreateFilterContext(CreateContext("staff"));

        new RequireRoleAttribute("admin", "staff").OnAuthorization(filterContext);

        Assert.Null(filterContext.Result);
    }

    private static DefaultHttpContext CreateContext(string roles)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["x-user-id"] = Guid.NewGuid().ToString();
        context.Request.Headers["x-user-roles"] = roles;
        return context;
    }

    private static AuthorizationFilterContext CreateFilterContext(HttpContext context)
    {
        var actionContext = new ActionContext(context, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }
}
