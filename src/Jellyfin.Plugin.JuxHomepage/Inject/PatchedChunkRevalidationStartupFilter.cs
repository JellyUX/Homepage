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
            var patched = revalidation.Prepare(context.Request);
            await nextMiddleware(context).ConfigureAwait(false);
            if (patched)
            {
                revalidation.Record(context);
            }
        });
        next(app);
    };
}
