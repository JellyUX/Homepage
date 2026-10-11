namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>Locates files checked in at the repository root (README, build.yaml, manifest.json).</summary>
internal static class RepoPaths
{
    /// <summary>Returns the repository root, found by walking up from the test assembly to the solution file.</summary>
    /// <returns>The absolute path of the repository root.</returns>
    internal static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JellyUX-Homepage.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repo root (JellyUX-Homepage.sln) from " + AppContext.BaseDirectory);
        }

        return dir.FullName;
    }
}
