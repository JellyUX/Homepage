using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// Builds a <see cref="WidgetService"/> for a test, with doubles for the collaborators the test does not drive:
/// no pending personalization, no access change, a section page cache over the given layout cache, and a mismatch
/// reporter that only logs.
/// </summary>
internal static class TestWidgetServices
{
    public static WidgetService Create(
        IWidgetRegistry registry,
        SessionCache sessionCache,
        WidgetLayoutResolver layoutResolver,
        Func<PluginConfiguration?> getConfiguration,
        IPersonalizationState? personalizationState = null,
        SectionResultCache? sectionResults = null,
        BinaryMismatchReporter? mismatchReporter = null) =>
        new(
            registry,
            sessionCache,
            layoutResolver,
            getConfiguration,
            NullLogger<WidgetService>.Instance,
            personalizationState ?? new NoPendingPersonalization(),
            sectionResults ?? new SectionResultCache(sessionCache),
            new NoAccessWatch(),
            mismatchReporter ?? new BinaryMismatchReporter(NullLogger<BinaryMismatchReporter>.Instance));

    /// <summary>A personalization state with no layout ever built before a first scoring.</summary>
    internal sealed class NoPendingPersonalization : IPersonalizationState
    {
        public bool TakePendingServed(Guid userId) => false;
    }

    /// <summary>An access watch that never sees a change.</summary>
    internal sealed class NoAccessWatch : IUserAccessWatch
    {
        public void Check(Guid userId)
        {
        }
    }
}
