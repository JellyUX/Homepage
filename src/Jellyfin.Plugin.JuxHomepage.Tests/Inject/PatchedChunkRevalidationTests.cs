using Jellyfin.Plugin.JuxHomepage.Inject;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

    // Bench of 1e4c98c: the ETag read when the inner middlewares returned was the native one, so the native
    // validators were kept after the first 200 and got 304 again.
    [Fact]
    public async Task NativeValidators_GetThePatchedChunk_AfterItWasServed()
    {
        var pipeline = Pipeline(out _);
        await Send(pipeline, LoadSectionsPath);

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

    // R11 of phase 11, measured on 10.11.11: FileTransformation also answers 304 to the native validators of
    // index.html, which it serves no-cache, so a browser revalidates it on every visit; the folder URL is the same
    // file (DefaultFiles maps it to index.html after this middleware).
    [Theory]
    [InlineData("/web/index.html")]
    [InlineData("/web/")]
    public async Task IndexHtml_NativeValidators_GetThePatchedPage(string path)
    {
        var pipeline = Pipeline(out _, patterns: ["index.html"]);
        await Send(pipeline, path);

        var native = await Send(pipeline, path, NativeETag, NativeDate);
        var own = await Send(pipeline, path, PatchedETag);

        Assert.Equal(200, native.StatusCode);
        Assert.Equal(304, own.StatusCode);
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

        // The order measured on 10.11.11 and 12.4: FileTransformation's startup filter is registered before ours, so
        // its middleware wraps ours, which wraps the static files.
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.Use(FileTransformationAsMeasured);
        new PatchedChunkRevalidationStartupFilter(revalidation).Configure(builder => builder.Run(StaticFilesAsMeasured))(app);
        return app.Build();
    }

    // StaticFileMiddleware: 304 to the validators of the file on disk (the native chunk), 200 otherwise.
    private static Task StaticFilesAsMeasured(HttpContext context)
    {
        var native = context.Request.Headers.IfNoneMatch.ToString() == NativeETag || context.Request.Headers.IfModifiedSince.ToString() == NativeDate;
        context.Response.StatusCode = native ? 304 : 200;
        context.Response.Headers.ETag = NativeETag;
        return Task.CompletedTask;
    }

    // FileTransformation around it: 304 to its own ETag, the inner 304 passed through, 200 otherwise; it writes its
    // own ETag (a hash of the patched body) when the response starts, after the inner middlewares returned.
    private static async Task FileTransformationAsMeasured(HttpContext context, RequestDelegate next)
    {
        var isPatched = !context.Request.Path.Value!.Contains("10092", StringComparison.Ordinal);
        await next(context);
        if (isPatched)
        {
            context.Response.StatusCode = context.Request.Headers.IfNoneMatch.ToString() == PatchedETag ? 304 : context.Response.StatusCode;
            context.Response.Headers.ETag = PatchedETag;
        }

        await ((StartableResponse)context.Features.Get<IHttpResponseFeature>()!).StartAsync();
    }

    private static async Task<HttpResponse> Send(RequestDelegate pipeline, string path, string? ifNoneMatch = null, string? ifModifiedSince = null)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartableResponse());
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

    // A response feature that runs its OnStarting callbacks when the test server starts the response.
    private sealed class StartableResponse : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

        public async Task StartAsync()
        {
            foreach (var (callback, state) in Enumerable.Reverse(_starting))
            {
                await callback(state);
            }
        }
    }
}
