using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.JuxHomepage.Inject;

/// <summary>
/// Puts <see cref="PatchedChunkRevalidation"/> in front of the web client's static files, FileTransformation
/// included (H-01, DR-027).
/// </summary>
/// <param name="revalidation">The patched chunks and the ETags they were sent with.</param>
public sealed class PatchedChunkRevalidationStartupFilter(PatchedChunkRevalidation revalidation) : IStartupFilter
{
    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (revalidation.Prepare(context.Request))
            {
                // FileTransformation's own startup filter wraps this middleware and writes the ETag of the patched
                // body after the inner middlewares returned: the ETag sent is the one the response starts with.
                context.Response.OnStarting(() =>
                {
                    revalidation.Record(context);
                    return Task.CompletedTask;
                });
            }

            await nextMiddleware(context).ConfigureAwait(false);
        });
        next(app);
    };
}
