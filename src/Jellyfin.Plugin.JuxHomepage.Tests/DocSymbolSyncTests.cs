using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

// CI guard (TODO_V5 PROC-10.a): CONTRIBUTING.md kept citing RegisterNativeWidget long after the helper
// was renamed, with nothing catching it. Every code symbol, repository path and jux.* key a versioned
// document cites between backticks must still exist, so a rename fails the build instead of the reader.
public sealed class DocSymbolSyncTests
{
    private const string PluginDir = "src/Jellyfin.Plugin.JuxHomepage";

    // PascalCase symbol, optionally dotted, with an optional generic argument and empty call parentheses:
    // `IWidget`, `ConnectedWidgetBase<T>`, `PluginServiceRegistrator.RegisterServices()`.
    private static readonly Regex Symbol = new(@"^([A-Z]\w*(?:\.[A-Z]\w*)*)(?:<\w+>)?(?:\(\))?$", RegexOptions.Compiled);

    // Repository path made of plain segments: `docker/docker-compose.yml`, `Widgets/Native/`.
    private static readonly Regex RepoPath = new(@"^[\w.-]+(?:/[\w.-]+)*/?$", RegexOptions.Compiled);

    // Widget type or localization key: `jux.connected.discover-movies`.
    private static readonly Regex JuxKey = new(@"^jux\.[a-z-]+(?:\.[a-z-]+)+$", RegexOptions.Compiled);

    public static TheoryData<string> Documents()
    {
        var root = RepoPaths.Root();
        var data = new TheoryData<string> { "CONTRIBUTING.md", "README.md" };
        foreach (var file in Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories))
        {
            data.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryCitedIdentifier_ExistsInTheRepository(string document)
    {
        var root = RepoPaths.Root();
        var sources = ReadAll(root, "src", "*.cs");
        var keys = sources + ReadAll(root, PluginDir, "*.json") + ReadAll(root, PluginDir, "*.js");

        var missing = new List<string>();
        foreach (var span in CodeSpans(File.ReadAllText(Path.Combine(root, document))))
        {
            var symbol = Symbol.Match(span);
            if (symbol.Success)
            {
                missing.AddRange(symbol.Groups[1].Value.Split('.')
                    .Where(name => !Regex.IsMatch(sources, $@"\b{Regex.Escape(name)}\b"))
                    .Select(name => $"`{span}` ({name})"));
            }
            else if (JuxKey.IsMatch(span))
            {
                if (!keys.Contains(span, StringComparison.Ordinal))
                {
                    missing.Add($"`{span}`");
                }
            }
            else if (IsRepoPath(root, span) && !PathExists(root, span))
            {
                missing.Add($"`{span}`");
            }
        }

        Assert.True(missing.Count == 0, $"{document} cites identifiers that no longer exist: {string.Join(", ", missing)}");
    }

    // TODO_V5 PROC-10.b: three server warnings sent administrators to CLAUDE.md, a file the repository
    // does not ship. The code and the documents may only point to what a clone contains.
    [Fact]
    public void NoVersionedFile_PointsToCLAUDEmd()
    {
        var root = RepoPaths.Root();
        var offenders = new[] { ("src", "*.cs"), ("src", "*.js"), ("docs", "*.md") }
            .SelectMany(source => SourceFiles(root, source.Item1, source.Item2))
            .Append(Path.Combine(root, "README.md"))
            .Append(Path.Combine(root, "CONTRIBUTING.md"))
            .Where(file => Path.GetFileName(file) != nameof(DocSymbolSyncTests) + ".cs")
            .Where(file => File.ReadAllText(file).Contains("CLAUDE.md", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.True(offenders.Count == 0, $"These files point to the unversioned CLAUDE.md: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EveryContributingSectionCitedByTheCode_Exists()
    {
        var root = RepoPaths.Root();
        var headings = Regex.Matches(File.ReadAllText(Path.Combine(root, "CONTRIBUTING.md")), @"^## (.+?)\r?$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var cited = SourceFiles(root, "src", "*.cs").Concat(SourceFiles(root, "src", "*.js"))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"CONTRIBUTING\.md ['""]([^'""]+)['""]").Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(cited);
        Assert.All(cited, section => Assert.Contains(section, headings));
    }

    private static IEnumerable<string> CodeSpans(string markdown)
    {
        // Fenced blocks hold commands, not citations.
        var prose = Regex.Replace(markdown, "```[\\s\\S]*?```", string.Empty);
        return Regex.Matches(prose, "`([^`\n]+)`").Select(m => m.Groups[1].Value.Trim()).Distinct();
    }

    // A slash also appears in TMDb routes (`tv/on_the_air`) and Docker images (`jellyfin/jellyfin`): a span is
    // a repository path only when its first segment is a directory of the repository or of the plugin.
    private static bool IsRepoPath(string root, string span) =>
        span.Contains('/', StringComparison.Ordinal)
        && RepoPath.IsMatch(span)
        && !span.Contains("..", StringComparison.Ordinal)
        && PathExists(root, span.Split('/')[0]);

    private static bool PathExists(string root, string path)
    {
        var relative = path.TrimEnd('/');
        return new[] { root, Path.Combine(root, PluginDir) }
            .Select(baseDir => Path.Combine(baseDir, relative))
            .Any(full => File.Exists(full) || Directory.Exists(full));
    }

    private static string ReadAll(string root, string dir, string pattern) =>
        string.Join('\n', SourceFiles(root, dir, pattern).Select(File.ReadAllText));

    private static IEnumerable<string> SourceFiles(string root, string dir, string pattern) =>
        Directory.GetFiles(Path.Combine(root, dir), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
