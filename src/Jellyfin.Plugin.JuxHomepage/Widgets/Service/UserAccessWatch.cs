using System.Collections.Concurrent;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Drops a user's cached home screen, and the scores their personalized sections were chosen from,
/// when an administrator changes what the user may see (libraries, parental rating, tags, unrated items). Jellyfin raises no event for a policy change
/// (<c>UserManager.UpdatePolicyAsync</c> in 10.11.9 saves without one) and reads the user from the
/// database on each <see cref="IUserManager.GetUserById"/>, so the user's access is read again at most
/// once per <see cref="CheckInterval"/> and compared with the previous read: a cached layout or section
/// page is served at most that long after the change (R11 review, DEC-P07-15).
/// </summary>
public sealed class UserAccessWatch
{
    /// <summary>The longest a user's access goes unread while their home screen is requested.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<Guid, Seen> _seen = new();
    private readonly IUserManager _userManager;
    private readonly SessionCache _sessionCache;
    private readonly ScoringService _scoringService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UserAccessWatch> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserAccessWatch"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="sessionCache">The layout cache; invalidating a user also drops their section pages.</param>
    /// <param name="scoringService">The scores computed from the items the user could see.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time source. Defaults to <see cref="TimeProvider.System"/>.</param>
    public UserAccessWatch(IUserManager userManager, SessionCache sessionCache, ScoringService scoringService, ILogger<UserAccessWatch> logger, TimeProvider? timeProvider = null)
    {
        _userManager = userManager;
        _sessionCache = sessionCache;
        _scoringService = scoringService;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Reads the user's access when it was last read more than <see cref="CheckInterval"/> ago, and
    /// drops their cached home screen when it changed.
    /// </summary>
    /// <param name="userId">The user.</param>
    public void Check(Guid userId)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (_seen.TryGetValue(userId, out var seen) && now - seen.ReadAt < CheckInterval)
        {
            return;
        }

        string access;
        try
        {
            var user = _userManager.GetUserById(userId);
            if (user is null)
            {
                return;
            }

            access = AccessOf(user);
        }
        catch (Exception ex)
        {
            // The home screen is still served; the access is read again on the next request.
            _logger.LogWarning(ex, "Failed to read the access of user {UserId}.", userId);
            return;
        }

        _seen[userId] = new Seen(access, now);
        if (seen is not null && !string.Equals(seen.Access, access, StringComparison.Ordinal))
        {
            // Scores first: the layout rebuilt after the invalidation must not read the former ones.
            _scoringService.Forget(userId);
            _sessionCache.Invalidate(userId);
        }
    }

    /// <summary>What the user may see, as one comparable string.</summary>
    private static string AccessOf(User user) => string.Join(
        '|',
        user.HasPermission(PermissionKind.IsAdministrator),
        user.HasPermission(PermissionKind.EnableAllFolders),
        user.HasPermission(PermissionKind.EnableAllChannels),
        user.MaxParentalRatingScore,
        user.MaxParentalRatingSubScore,
        string.Join(',', user.GetPreference(PreferenceKind.EnabledFolders)),
        string.Join(',', user.GetPreference(PreferenceKind.EnabledChannels)),
        string.Join(',', user.GetPreference(PreferenceKind.BlockedTags)),
        string.Join(',', user.GetPreference(PreferenceKind.AllowedTags)),
        string.Join(',', user.GetPreference(PreferenceKind.BlockUnratedItems)));

    private sealed record Seen(string Access, DateTime ReadAt);
}
