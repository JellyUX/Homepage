namespace Jellyfin.Plugin.JuxHomepage.Watchlist.Models;

/// <summary>
/// The answer of <c>GET JuxHomepage/Watchlist/Ids</c>: the liked ids the card buttons start from,
/// and whether JellyUX runs for the user (TODO_V4.md 8.3, F-36). Every page reads it once, so the
/// client learns the user's choice without a request of its own.
/// </summary>
/// <param name="Ids">Every movie and series id currently in the user's Watchlist.</param>
/// <param name="Enabled">False when JellyUX is off globally or the user turned it off for themselves.</param>
public sealed record WatchlistIdsResult(IReadOnlyList<Guid> Ids, bool Enabled);
