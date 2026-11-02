using Jellyfin.Plugin.JuxHomepage.Inject;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Inject;

/// <summary>
/// H-01 (DR-009, DR-027): FileTransformation answers 304 to the validators of the native chunk, so a browser that
/// cached the chunk before JellyUX patched it keeps the native home. In front of it, only the ETag this server sent
/// for the patched chunk may reach a revalidation.
/// </summary>
public sealed class PatchedChunkRevalidationTests
{
    private const string PatchedETag = "\"ce279d32252de685\"";
    private const string NativeETag = "\"1dcf5cb450e4e06\"";
    private const string NativeDate = "Sat, 06 Jun 2026 15:43:50 GMT";
    private const string LoadSectionsPath = "/web/56213.a6cde3c8ba80d7030952.chunk.js";

    [Fact]
    public async Task NativeValidators_GetThePatchedChunk()
    {
        var pipeline = Pipeline(out _);

        var response = await Send(pipeline, LoadSectionsPath, NativeETag, NativeDate);

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task TheETagThisServerSent_StillRevalidates()
    {
        var pipeline = Pipeline(out _);
        await Send(pipeline, LoadSectionsPath);

        var response = await Send(pipeline, LoadSectionsPath, PatchedETag, NativeDate);

        Assert.Equal(304, response.StatusCode);
    }

    [Fact]
    public async Task ARecordedETag_IsKeptForItsOwnPathOnly()
    {
        var pipeline = Pipeline(out _);
        await Send(pipeline, LoadSectionsPath);

        var response = await Send(pipeline, "/web/home-html.8ce38bc7d6dc073656d4.chunk.js", PatchedETag);

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task AnotherFile_KeepsItsValidators()
    {
        var pipeline = Pipeline(out _);

        var response = await Send(pipeline, "/web/10092.c37bcafcc8eb7c5c080e.chunk.js", NativeETag, NativeDate);

        Assert.Equal(304, response.StatusCode);
    }

    [Fact]
    public async Task NoPatternRegistered_LeavesEveryRequestAsItIs()
    {
        var pipeline = Pipeline(out _, patterns: []);

        var response = await Send(pipeline, LoadSectionsPath, NativeETag);

        Assert.Equal(304, response.StatusCode);
    }

    [Fact]
    public void Patterns_MatchTheWholeFileName()
    {
        var revalidation = new PatchedChunkRevalidation();
        revalidation.AddPattern("56213\\.[^.]+\\.chunk\\.js");

        Assert.True(revalidation.IsPatched("56213.a6cde3c8ba80d7030952.chunk.js"));
        Assert.False(revalidation.IsPatched("156213.a6cde3c8ba80d7030952.chunk.js"));
        Assert.False(revalidation.IsPatched("56213.a6cde3c8ba80d7030952.chunk.js.map"));
    }

    private static RequestDelegate Pipeline(out PatchedChunkRevalidation revalidation, string[]? patterns = null)
    {
        revalidation = new PatchedChunkRevalidation();
        foreach (var pattern in patterns ?? ["56213\\.[^.]+\\.chunk\\.js", "home-html\\.[^.]+\\.chunk\\.js"])
        {
            revalidation.AddPattern(pattern);
        }

        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        new PatchedChunkRevalidationStartupFilter(revalidation).Configure(builder => builder.Run(FileTransformationAsMeasured))(app);
        return app.Build();
    }

    // FileTransformation as measured on 10.11.11 and 12.4: 304 to any validator, 200 with its own ETag otherwise.
    private static Task FileTransformationAsMeasured(HttpContext context)
    {
        context.Response.Headers.ETag = context.Request.Path.Value!.Contains("10092", StringComparison.Ordinal) ? NativeETag : PatchedETag;
        context.Response.StatusCode = context.Request.Headers.IfNoneMatch.Count > 0 || context.Request.Headers.IfModifiedSince.Count > 0 ? 304 : 200;
        return Task.CompletedTask;
    }

    private static async Task<HttpResponse> Send(RequestDelegate pipeline, string path, string? ifNoneMatch = null, string? ifModifiedSince = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        if (ifNoneMatch is not null)
        {
            context.Request.Headers.IfNoneMatch = ifNoneMatch;
        }

        if (ifModifiedSince is not null)
        {
            context.Request.Headers.IfModifiedSince = ifModifiedSince;
        }

        await pipeline(context);
        return context.Response;
    }
}
