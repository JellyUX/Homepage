using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

// CI guard (Phase 12.1 of TODO_V2.md): the README widget table drifted silently for several phases
// during V1 with nothing catching it. This test fails the build if a widget is registered/removed in
// PluginServiceRegistrator without a matching update to the README table (and to the "N widgets across"
// prose sentence right above it, a second drift-prone spot found while writing this test).
// Extended in Phase 13.4 to also guard the MkDocs widget pages under docs/widgets/.
public sealed class ReadmeWidgetTableSyncTests
{
    private const string RegisterWidgetPattern = @"RegisterWidget<\w+>\(registry";
    private const string TableHeader = "| Widget | Category | Description |";

    [Fact]
    public void RegisteredWidgetCount_MatchesReadmeWidgetTableRowCount()
    {
        var repoRoot = RepoPaths.Root();

        var registrarSource = File.ReadAllText(Path.Combine(
            repoRoot, "src", "Jellyfin.Plugin.JuxHomepage", "PluginServiceRegistrator.cs"));
        var registeredCount = Regex.Matches(registrarSource, RegisterWidgetPattern).Count;

        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        var tableRowCount = CountWidgetTableRows(readme);

        Assert.Equal(registeredCount, tableRowCount);
    }

    [Fact]
    public void WidgetDocsPageCount_MatchesRegisteredWidgetCount()
    {
        var repoRoot = RepoPaths.Root();

        var registrarSource = File.ReadAllText(Path.Combine(
            repoRoot, "src", "Jellyfin.Plugin.JuxHomepage", "PluginServiceRegistrator.cs"));
        var registeredCount = Regex.Matches(registrarSource, RegisterWidgetPattern).Count;

        var docsWidgetsDir = Path.Combine(repoRoot, "docs", "widgets");
        var docsPageCount = Directory.GetFiles(docsWidgetsDir, "*.md", SearchOption.AllDirectories).Length;

        Assert.Equal(registeredCount, docsPageCount);
    }

    [Fact]
    public void ReadmeWidgetCountSentence_MatchesReadmeWidgetTableRowCount()
    {
        var repoRoot = RepoPaths.Root();
        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));

        var tableRowCount = CountWidgetTableRows(readme);

        var match = Regex.Match(readme, @"(\d+) widgets across");
        Assert.True(match.Success, "Expected to find an 'N widgets across ...' sentence in README.md.");
        var proseCount = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(tableRowCount, proseCount);
    }

    private static int CountWidgetTableRows(string readme)
    {
        var headerIndex = readme.IndexOf(TableHeader, StringComparison.Ordinal);
        Assert.True(headerIndex >= 0, $"Expected to find the widget table header '{TableHeader}' in README.md.");

        var lines = readme[headerIndex..].Split('\n');
        // lines[0] = header, lines[1] = "|---|---|---|" separator, data rows follow until a blank line.
        var rowCount = 0;
        for (var i = 2; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (!line.StartsWith("| ", StringComparison.Ordinal))
            {
                break;
            }

            rowCount++;
        }

        return rowCount;
    }
}
