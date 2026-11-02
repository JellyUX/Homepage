using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JuxHomepage.Inject;

/// <summary>
/// Keeps a browser from revalidating a cached native chunk against the chunk FileTransformation patches (H-01,
/// DR-027). FileTransformation answers 304 to the validators of the native file (measured on 10.11.11 and 12.4,
/// both patched chunks), so a browser that cached the chunk before JellyUX patched it kept the native home. On a
/// patched chunk, only the ETag this server last sent with a 200 for that path may reach a revalidation: any other
/// If-None-Match, and every If-Modified-Since (the patched chunk carries no Last-Modified), is dropped, so the
/// request gets the patched body.
/// </summary>
public sealed class PatchedChunkRevalidation
{
    private readonly ConcurrentDictionary<string, string> _sentETags = new(StringComparer.Ordinal);
    private ImmutableArray<Regex> _patterns = [];

    /// <summary>Registers the file-name pattern of a chunk FileTransformation patches.</summary>
    /// <param name="fileNamePattern">The pattern given to FileTransformation, matched against the whole file name.</param>
    public void AddPattern(string fileNamePattern) =>
        ImmutableInterlocked.Update(ref _patterns, p => p.Add(new Regex($"^(?:{fileNamePattern})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))));

    /// <summary>Returns whether a file name is one of the patched chunks.</summary>
    /// <param name="fileName">The file name, without its folder.</param>
    /// <returns>True for a patched chunk.</returns>
    public bool IsPatched(string fileName) => _patterns.Any(p => p.IsMatch(fileName));

    /// <summary>
    /// Drops the validators a patched chunk must not be revalidated with.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>True when the request is for a patched chunk: its response is then passed to <see cref="Record"/>.</returns>
    public bool Prepare(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        if (!IsPatched(path[(path.LastIndexOf('/') + 1)..]))
        {
            return false;
        }

        request.Headers.Remove("If-Modified-Since");
        if (!_sentETags.TryGetValue(path, out var sent) || request.Headers.IfNoneMatch.ToString() != sent)
        {
            request.Headers.Remove("If-None-Match");
        }

        return true;
    }

    /// <summary>Remembers the ETag a patched chunk was sent with.</summary>
    /// <param name="context">The context of a request <see cref="Prepare"/> accepted, once answered.</param>
    public void Record(HttpContext context)
    {
        var etag = context.Response.Headers.ETag.ToString();
        if (context.Response.StatusCode == StatusCodes.Status200OK && etag.Length > 0)
        {
            _sentETags[context.Request.Path.Value ?? string.Empty] = etag;
        }
    }
}
