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
        // Code only: a name that survives in a comment (this file's own header among them) is not a symbol.
        var sources = string.Join('\n', SourceFiles(root, "src", "*.cs").Select(file => WithoutComments(File.ReadAllText(file))));
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
        var offenders = new[] { "src", "docs", "bench", "e2e", "test", "ci" }
            .SelectMany(dir => new[] { "*.cs", "*.js", "*.mjs", "*.md" }.SelectMany(pattern => SourceFiles(root, dir, pattern)))
            .Append(Path.Combine(root, "README.md"))
            .Append(Path.Combine(root, "CONTRIBUTING.md"))
            .Where(file => !IsThisFile(file))
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

    public static TheoryData<string> FailurePathRows()
    {
        var contributing = File.ReadAllText(Path.Combine(RepoPaths.Root(), "CONTRIBUTING.md"));
        var section = Regex.Match(contributing, @"^## Failure Paths\r?$([\s\S]*?)^## ", RegexOptions.Multiline).Groups[1].Value;
        return new(Regex.Matches(section, @"^\| (?!Boundary \||---)(.+) \|\r?$", RegexOptions.Multiline).Select(m => m.Groups[1].Value));
    }

    // PROC-06.c: a model test the Failure Paths table cites must exist, so the table cannot keep pointing
    // to a renamed test or claim a test it does not have.
    [Theory]
    [MemberData(nameof(FailurePathRows))]
    public void EveryFailurePathModelTest_Exists(string row)
    {
        var problems = ModelTestProblems(row, RepoPaths.Root());

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void ModelTestProblems_ReportsAMissingTestADescriptionNoFileHoldsAndAnUntrackedGap()
    {
        var root = RepoPaths.Root();

        Assert.Empty(ModelTestProblems("Boundary | `DocSymbolSyncTests.EveryFailurePathModelTest_Exists`, `test/jux-core.test.js`, \"answers with the server, and rejects once the limit passes without an answer\" | proves", root));
        Assert.Empty(ModelTestProblems("Boundary | to be written (FIAB-02) | proves", root));
        Assert.Empty(ModelTestProblems("Boundary | to be written (FIAB-02.a, DR-001, DEC-P12-6) | proves", root));
        Assert.Equal(3, ModelTestProblems("Boundary | `DocSymbolSyncTests.NoSuchTest`, `test/jux-core.test.js`, \"no such description\" | proves", root)
            .Concat(ModelTestProblems("Boundary | to be written | proves", root)).Count());
    }

    // The model test cell of a table row: C# tests as `Class.Method`, script tests as `path`, "description",
    // a gap as "to be written (registry id)".
    private static List<string> ModelTestProblems(string row, string root)
    {
        var cell = row.Split(" | ")[1];
        var problems = new List<string>();
        if (cell.StartsWith("to be written", StringComparison.Ordinal))
        {
            // The registry ids of ci/static-checks.mjs (finding, criterion, decision record, phase decision).
            const string id = @"(?:[A-Z]+-\d{2}(?:\.[a-z])?|DR-\d{3}|DEC-P\d{2}-\d+)";
            if (!Regex.IsMatch(cell, $@"\({id}(?:, {id})*\)"))
            {
                problems.Add($"\"{cell}\" names no registry id that will write the test");
            }

            return problems;
        }

        var testsDir = Path.Combine(root, "src", "Jellyfin.Plugin.JuxHomepage.Tests");
        string? script = null;
        foreach (Match part in Regex.Matches(cell, "`([^`]+)`|\"([^\"]+)\""))
        {
            if (part.Groups[2].Success)
            {
                var text = script is null ? string.Empty : File.ReadAllText(Path.Combine(root, script));
                if (!text.Contains($"'{part.Groups[2].Value}'", StringComparison.Ordinal) && !text.Contains($"\"{part.Groups[2].Value}\"", StringComparison.Ordinal))
                {
                    problems.Add($"{script ?? "no file"} has no test \"{part.Groups[2].Value}\"");
                }
            }
            else if (part.Groups[1].Value.Contains('/', StringComparison.Ordinal))
            {
                script = part.Groups[1].Value;
            }
            else if (part.Groups[1].Value.Split('.') is [var type, var method])
            {
                var declared = SourceFiles(root, Path.GetRelativePath(root, testsDir), "*.cs")
                    .Select(File.ReadAllText)
                    .Any(text => Regex.IsMatch(text, $@"\bclass {type}\b")
                        && Regex.IsMatch(text, $@"\[(?:Fact|Theory)\b[^\]]*\](?:\s*\[[^\]]*\])*\s*public (?:async Task|void) {method}\("));
                if (!declared)
                {
                    problems.Add($"no [Fact] or [Theory] {type}.{method}");
                }
            }
        }

        return problems;
    }

    [Fact]
    public void WithoutComments_KeepsStringsThatLookLikeComments_AndTheCodeAfterThem()
    {
        // "/*" in a string literal (TransformationPatchesTests) used to open a block comment that swallowed
        // the code up to the next "*/", in whichever file came next on the runner (CI on Linux, phase 05).
        const string code = "var a = \"/*\"; var b = '\"'; Keep(); // gone\n/* gone */ var c = @\"x//\"\"y\"; AlsoKept();";

        var stripped = WithoutComments(code);

        Assert.Contains("Keep()", stripped, StringComparison.Ordinal);
        Assert.Contains("AlsoKept()", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("gone", stripped, StringComparison.Ordinal);
    }

    // String and character literals are matched before comments, so their content is never read as one.
    private static string WithoutComments(string code) =>
        Regex.Replace(
            code,
            @"@""(?:""""|[^""])*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])'|//[^\n]*|/\*[\s\S]*?\*/",
            m => m.Value[0] == '/' ? string.Empty : m.Value);

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

    private static bool IsThisFile(string file) => Path.GetFileName(file) == nameof(DocSymbolSyncTests) + ".cs";

    private static IEnumerable<string> SourceFiles(string root, string dir, string pattern) =>
        Directory.GetFiles(Path.Combine(root, dir), pattern, SearchOption.AllDirectories)
            .Where(f => !new[] { "bin", "obj", "node_modules", ".work" }
                .Any(skip => f.Contains($"{Path.DirectorySeparatorChar}{skip}{Path.DirectorySeparatorChar}", StringComparison.Ordinal)));
}
