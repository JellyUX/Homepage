using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// A <see cref="Movie"/> whose per-user visibility is set by the test. The real
/// <c>IsVisibleStandalone</c> reaches Jellyfin's static library and Live TV managers, which are not
/// wired in unit tests.
/// </summary>
internal sealed class TestMovie : Movie
{
    public bool VisibleToUser { get; init; } = true;

    public override bool IsVisibleStandalone(User user) => VisibleToUser;
}

/// <summary>A <see cref="Series"/> whose per-user visibility is set by the test (see <see cref="TestMovie"/>).</summary>
internal sealed class TestSeries : Series
{
    public bool VisibleToUser { get; init; } = true;

    public override bool IsVisibleStandalone(User user) => VisibleToUser;
}

/// <summary>A <see cref="BoxSet"/> whose per-user visibility is set by the test (see <see cref="TestMovie"/>).</summary>
internal sealed class TestBoxSet : BoxSet
{
    public bool VisibleToUser { get; init; } = true;

    public override bool IsVisibleStandalone(User user) => VisibleToUser;
}
