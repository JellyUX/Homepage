using System;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.JuxHomepage.Controllers;

/// <summary>
/// Marks every response of an authenticated action <c>Cache-Control: private, no-cache</c>: it carries one
/// user's data, so no shared cache may keep it and the browser revalidates it on every read (S8). Without
/// the header a browser may reuse a JSON response heuristically and paint rows that no longer match the
/// server. Anonymous actions (web assets, translations) set their own caching and are left untouched.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class PrivateNoCacheAttribute : ResultFilterAttribute
{
    /// <summary>The header value written on authenticated responses.</summary>
    public const string HeaderValue = "private, no-cache";

    /// <inheritdoc/>
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        context.HttpContext.Response.Headers.CacheControl = HeaderValue;
    }
}
