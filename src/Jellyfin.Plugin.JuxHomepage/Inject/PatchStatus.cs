namespace Jellyfin.Plugin.JuxHomepage.Inject;

/// <summary>
/// Whether the loadSections chunk is patched, as found by the startup dry run.
/// </summary>
public enum LoadSectionsPatchState
{
    /// <summary>Not checked: FileTransformation is missing, or startup has not run yet.</summary>
    Unknown,

    /// <summary>A loadSections chunk is patched: the home page renders JellyUX sections.</summary>
    Patched,

    /// <summary>The chunk holds ",loadSections:" but a minified token is missing (Jellyfin Web drift).</summary>
    HookNotFound,

    /// <summary>No chunk of the web client holds ",loadSections:".</summary>
    NoChunk
}

/// <summary>
/// The patch state the startup dry run found (FIAB-04): read by the client's stale chunk guard, which
/// reloads only for a chunk the server patches, and by the admin warnings. The dry run reads the same
/// files FileTransformation serves, so it is the state the client receives.
/// </summary>
public sealed class PatchStatus
{
    private volatile LoadSectionsPatchState _loadSections = LoadSectionsPatchState.Unknown;
    private volatile object? _tabsAnchor;

    /// <summary>Gets the loadSections chunk state.</summary>
    public LoadSectionsPatchState LoadSections => _loadSections;

    /// <summary>Gets whether the home-html chunk holds the tab anchor; null when not checked.</summary>
    public bool? TabsAnchor => (bool?)_tabsAnchor;

    /// <summary>Records the loadSections chunk state.</summary>
    /// <param name="state">The state found.</param>
    public void SetLoadSections(LoadSectionsPatchState state) => _loadSections = state;

    /// <summary>Records whether the home-html chunk holds the tab anchor.</summary>
    /// <param name="found">Whether the anchor was found.</param>
    public void SetTabsAnchor(bool found) => _tabsAnchor = found;

    /// <summary>
    /// Combines the outcomes of the loadSections chunks found: patched when one of them is.
    /// </summary>
    /// <param name="outcomes">The outcome of each chunk holding ",loadSections:".</param>
    /// <returns>The combined state.</returns>
    internal static LoadSectionsPatchState Combine(IReadOnlyCollection<LoadSectionsOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return LoadSectionsPatchState.NoChunk;
        }

        if (outcomes.Contains(LoadSectionsOutcome.Patched))
        {
            return LoadSectionsPatchState.Patched;
        }

        return outcomes.Contains(LoadSectionsOutcome.HookNotFound)
            ? LoadSectionsPatchState.HookNotFound
            : LoadSectionsPatchState.NoChunk;
    }
}

/// <summary>
/// The patch state sent to the client: <see cref="LoadSectionsPatchState"/> as its name.
/// </summary>
/// <param name="LoadSections">"Patched", "HookNotFound", "NoChunk" or "Unknown".</param>
/// <param name="TabsAnchor">Whether the tab panes can be spliced in; null when not checked.</param>
public sealed record PatchStatusView(string LoadSections, bool? TabsAnchor);
