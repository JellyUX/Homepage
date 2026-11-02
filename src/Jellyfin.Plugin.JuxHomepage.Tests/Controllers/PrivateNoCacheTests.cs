using System.Reflection;
using Jellyfin.Plugin.JuxHomepage.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Controllers;

// S8 (V17): a response that carries a user's data must never be served from a browser or proxy cache
// without revalidation. Every authenticated action answers 'Cache-Control: private, no-cache'; the two
// anonymous actions keep the headers they set themselves.
public sealed class PrivateNoCacheTests
{
    private static readonly string[] AnonymousActions =
    [
        "JuxHomepageController.GetAsset",
        "JuxHomepageController.GetLocalizations",
    ];

    [Fact]
    public void EveryController_CarriesThePrivateNoCacheFilter()
    {
        foreach (var controller in Controllers())
        {
            Assert.True(
                controller.GetCustomAttribute<PrivateNoCacheAttribute>() is not null,
                $"{controller.Name} does not carry [PrivateNoCache]");
        }
    }

    [Fact]
    public void OnlyTheAssetAndLocalizationActions_AreAnonymous()
    {
        var anonymous = Controllers()
            .SelectMany(c => Actions(c)
                .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                .Select(m => $"{c.Name}.{m.Name}"))
            .Order(StringComparer.Ordinal);

        Assert.Equal(AnonymousActions, anonymous);
    }

    [Fact]
    public void AuthenticatedAction_AnswersPrivateNoCache()
    {
        var context = Executing([]);

        new PrivateNoCacheAttribute().OnResultExecuting(context);

        Assert.Equal("private, no-cache", context.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void AnonymousAction_KeepsItsOwnHeader()
    {
        var context = Executing([new AllowAnonymousAttribute()]);
        context.HttpContext.Response.Headers.CacheControl = "public, max-age=3600";

        new PrivateNoCacheAttribute().OnResultExecuting(context);

        Assert.Equal("public, max-age=3600", context.HttpContext.Response.Headers.CacheControl.ToString());
    }

    private static ResultExecutingContext Executing(IList<object> endpointMetadata)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor { EndpointMetadata = endpointMetadata });
        return new ResultExecutingContext(actionContext, [], new OkResult(), controller: new object());
    }

    private static IEnumerable<Type> Controllers() =>
        typeof(JuxHomepageController).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());
}
