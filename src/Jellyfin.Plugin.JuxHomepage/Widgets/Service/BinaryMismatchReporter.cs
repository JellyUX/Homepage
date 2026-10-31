using System.Collections.Concurrent;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Tells the administrator, in the dashboard activity log, that a section fails because the plugin build
/// does not match the server (a 10.11 build on a Jellyfin 12 server, COMPAT-01.a): a missing member or type
/// otherwise leaves the section empty, or out of the layout, with only the server log saying why. One
/// entry per widget type and per server start.
/// </summary>
public sealed class BinaryMismatchReporter
{
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);
    private readonly ILogger<BinaryMismatchReporter> _logger;
    private readonly IActivityManager? _activityManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryMismatchReporter"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="activityManager">The dashboard activity log; null only logs.</param>
    public BinaryMismatchReporter(ILogger<BinaryMismatchReporter> logger, IActivityManager? activityManager = null)
    {
        _logger = logger;
        _activityManager = activityManager;
    }

    /// <summary>
    /// Returns whether the exception, or one it wraps (an aggregate, a reflection call, a type initializer),
    /// is a missing member or type: the mark of a build made for another Jellyfin version.
    /// </summary>
    /// <param name="exception">The exception a widget threw.</param>
    /// <returns>The mismatch found, or null.</returns>
    public static Exception? FindMismatch(Exception exception)
    {
        var pending = new Stack<Exception>([exception]);
        while (pending.TryPop(out var current))
        {
            if (current is MissingMemberException or TypeLoadException)
            {
                return current;
            }

            if (current is AggregateException aggregate)
            {
                aggregate.InnerExceptions.ToList().ForEach(pending.Push);
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }

        return null;
    }

    /// <summary>
    /// Records the failure of a widget type in the activity log when it is a build mismatch, the first time
    /// for that type; an entry that could not be written is tried again at the next failure.
    /// </summary>
    /// <param name="widgetType">The widget type whose section failed.</param>
    /// <param name="exception">What it threw.</param>
    /// <returns>A task that completes once the entry is written or refused.</returns>
    public async Task ReportAsync(string widgetType, Exception exception)
    {
        if (_activityManager is null || FindMismatch(exception) is not { } mismatch || !_reported.TryAdd(widgetType, 0))
        {
            return;
        }

        try
        {
            await _activityManager.CreateAsync(new ActivityLog($"JellyUX Homepage: the {widgetType} section is unavailable", "JuxHomepageBinaryMismatch", Guid.Empty)
            {
                ShortOverview = mismatch.GetType().Name,
                Overview = $"{mismatch.Message} The installed JellyUX Homepage build does not match this Jellyfin version: install the build for your Jellyfin version and restart the server.",
                LogSeverity = LogLevel.Error
            }).ConfigureAwait(false);
        }
        catch (Exception logException)
        {
            _reported.TryRemove(widgetType, out _);
            _logger.LogError(logException, "Could not record the unavailable section '{WidgetType}' in the activity log.", widgetType);
        }
    }
}
