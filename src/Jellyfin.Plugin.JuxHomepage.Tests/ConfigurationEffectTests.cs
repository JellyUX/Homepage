using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// TODO_V5 PROC-06.c (rule R10): every configuration setting does something. For each property of the
/// configuration types: (a) production code outside the configuration types, the migrations
/// (<c>Plugin.cs</c>) and the admin page reads it; (b) production code outside the migrations writes it
/// (the admin or user settings page counts); (c) a [Fact] or [Theory] is named after it, and also names
/// its type when several configuration types declare that name (A-07). Read from the sources, so a
/// setting nobody reads, writes or tests fails here instead of shipping as a switch with no effect.
/// The widget <c>ExtraParams</c> keys are settings too (D-12.c): each key constant is read by production
/// code, and each <c>data-jux-*</c> attribute the scripts set from a setting is read by a stylesheet or a
/// script.
/// </summary>
public sealed class ConfigurationEffectTests
{
    private const string PluginDir = "src/Jellyfin.Plugin.JuxHomepage";

    private const string ModulesWrittenByAttribute =
        "written by the admin page through its data-module attribute (config.Modules[name] = checked)";

    private static readonly Type[] ConfigurationTypes =
        [typeof(PluginConfiguration), typeof(CacheConfig), typeof(ModulesConfig), typeof(WidgetConfig), typeof(UserConfiguration)];

    // Rules a setting is exempt from, each with its reason. An exemption the setting no longer needs fails.
    private static readonly Dictionary<string, string> Exemptions = new(StringComparer.Ordinal)
    {
        ["PluginConfiguration.SchemaVersion (a)"] = "stamped and read by the migrations only: it versions the file, it has no runtime effect",
        ["PluginConfiguration.SchemaVersion (b)"] = "stamped by the migrations only",
        ["UserConfiguration.SchemaVersion (a)"] = "stamped and read by the migrations only: it versions the file, it has no runtime effect",
        ["UserConfiguration.SchemaVersion (b)"] = "stamped by the migrations only",
        ["ModulesConfig.Tabs (b)"] = ModulesWrittenByAttribute,
        ["ModulesConfig.CardEnhancements (b)"] = ModulesWrittenByAttribute,
        ["ModulesConfig.SeriesFlatten (b)"] = ModulesWrittenByAttribute,
        ["ModulesConfig.Breadcrumb (b)"] = ModulesWrittenByAttribute,
    };

    // Settings frozen by QUAL-02 (D-05): still read from the files and carried by the migrations, read by
    // nothing else. See CONTRIBUTING.md "Configuration Schema Versioning".
    private static readonly string[] Frozen =
    [
        "WidgetConfig.AllowUserOverride",
        "WidgetConfig.MinInstances",
        "WidgetConfig.MaxInstances",
        "UserConfiguration.WidgetOverrides"
    ];

    private static readonly Lazy<Sources> Code = new(Sources.Load);

    private static readonly Lazy<HashSet<string>> SharedNames = new(() => ConfigurationTypes
        .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).Select(p => p.Name))
        .GroupBy(n => n, StringComparer.Ordinal)
        .Where(g => g.Count() > 1)
        .Select(g => g.Key)
        .ToHashSet(StringComparer.Ordinal));

    public static TheoryData<string> Settings()
    {
        var data = new TheoryData<string>();
        foreach (var setting in ConfigurationTypes.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                     .Where(p => p.CanWrite)
                     .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
                     .Where(s => !Frozen.Contains(s)))
        {
            data.Add(setting);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Settings))]
    public void EverySetting_IsReadWrittenAndTested(string setting)
    {
        Assert.Empty(Problems(setting));
    }

    [Fact]
    public void TestMethods_TakeFactsAndTheories_NotPublicHelpers()
    {
        const string source = "[Fact]\npublic void A_Enabled() { _ = new WidgetConfig(); }\n"
            + "public void Helper_Enabled() { }\n[Theory]\n[InlineData(1)]\npublic async Task B(int x) { }";

        Assert.Equal(["A_Enabled", "B"], Sources.TestMethods(source).Select(t => t.Name));
    }

    // A comment naming a type does not make the test use it (R11).
    [Fact]
    public void TestMethods_LeaveCommentsOutOfTheBody()
    {
        const string source = "[Fact]\npublic void A_Enabled()\n{\n    // UserConfiguration is not used here\n    _ = new WidgetConfig();\n}";

        Assert.DoesNotContain("UserConfiguration", Sources.TestMethods(source).Single().Body, StringComparison.Ordinal);
    }

    // Attributes the scripts set that nothing reads, each with the registry id that
    // gives it an effect. An exemption the attribute no longer needs fails.
    private static readonly Dictionary<string, string> UnreadAttributes = new(StringComparer.Ordinal)
    {
        ["data-jux-theme"] = "BKL-04: the seasonal preset Theme key reaches the section, no style draws it until V6 phase 18",
    };

    // Keyed "Class.Constant": two widgets may declare the same constant name (YearParam), and one read must
    // not stand for the other (R11).
    public static TheoryData<string> ExtraParamKeys() => new(Code.Value.AllFiles
        .SelectMany(f => Regex.Matches(f.Text, @"public const string (\w+Param) = """)
            .Select(m => $"{Regex.Matches(f.Text[..m.Index], @"\bclass (\w+)").Last().Groups[1].Value}.{m.Groups[1].Value}"))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(ExtraParamKeys))]
    public void EveryExtraParamKey_IsReadByProductionCode(string key)
    {
        var (type, constant) = (key.Split('.')[0], key.Split('.')[1]);
        // Qualified anywhere, or bare inside the declaring class and the classes deriving from it.
        var reads = Code.Value.ReaderFiles.Sum(f => Regex.Matches(f.Text, $@"\b{type}\.{constant}\b").Count
            + (Regex.IsMatch(f.Text, $@"\bclass {type}\b|:\s*{type}\b")
                ? Regex.Matches(f.Text, $@"(?<!\.)\b{constant}\b(?!\s*=\s*"")").Count
                : 0));

        Assert.True(reads > 0, $"{key}: no production code reads this ExtraParams key");
    }

    public static TheoryData<string> AttributesTheScriptsSet() => new(Sources.SetAttributes(Code.Value.Scripts));

    [Theory]
    [MemberData(nameof(AttributesTheScriptsSet))]
    public void EveryAttributeTheScriptsSet_IsReadByAStylesheetOrAScript(string attribute)
    {
        var read = Sources.ReadsAttribute(Code.Value.Scripts + "\n" + Code.Value.Styles, attribute);
        var exempt = UnreadAttributes.ContainsKey(attribute);

        Assert.True(read != exempt, read
            ? $"{attribute} is read now: remove its exemption"
            : $"{attribute} is set by the scripts but no stylesheet or script reads it: the setting behind it has no effect");
    }

    [Fact]
    public void Attributes_AreFoundSetAndRead_ThroughSetAttributeAndDataset()
    {
        const string scripts = "el.setAttribute('data-jux-theme', t);\nel.dataset.juxHasMore = '1';\nif (el.dataset.juxHasMore) { }";

        Assert.Equal(["data-jux-has-more", "data-jux-theme"], Sources.SetAttributes(scripts));
        Assert.True(Sources.ReadsAttribute(scripts, "data-jux-has-more"));
        Assert.False(Sources.ReadsAttribute(scripts, "data-jux-theme"));
        Assert.True(Sources.ReadsAttribute(".jux-widget-section[data-jux-theme=\"halloween\"] { }", "data-jux-theme"));
    }

    public static TheoryData<string> FrozenSettings() => new(Frozen);

    // A frozen setting that production code reads again, or that a page names, is a thawed one: it
    // needs the CONTRIBUTING.md policy changed first, double migrations included.
    [Theory]
    [MemberData(nameof(FrozenSettings))]
    public void FrozenSetting_IsReadByNoProductionCodeAndNamedByNoPage(string setting)
    {
        var name = setting.Split('.')[1];

        Assert.DoesNotMatch($@"\.{name}\b", Code.Value.Readers);
        Assert.DoesNotMatch($@"\b{name}\b", Code.Value.Scripts);
    }

    private static List<string> Problems(string setting)
    {
        var type = setting.Split('.')[0];
        var name = setting.Split('.')[1];
        var failed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["(a)"] = Regex.IsMatch(Code.Value.Readers, $@"\.{name}\b") ? string.Empty : "no production code outside the configuration reads it",
            ["(b)"] = Regex.IsMatch(Code.Value.Writers, $@"(\.|\b){name}\s*(=(?!=)|:)") ? string.Empty : "no production code outside the migrations writes it",
            // A name several configuration types declare (Enabled, SchemaVersion) needs a test that also names
            // its type, so UserConfiguration.Enabled is not met by a test of WidgetConfig.Enabled (A-07).
            ["(c)"] = Code.Value.Tests.Any(test => test.Name.Contains(name, StringComparison.Ordinal)
                    && (!SharedNames.Value.Contains(name) || Regex.IsMatch(test.Body, $@"\b{type}\b")))
                ? string.Empty
                : SharedNames.Value.Contains(name) ? $"no test is named after it and uses {type}" : "no test is named after it",
        };

        var problems = new List<string>();
        foreach (var (rule, problem) in failed)
        {
            var exempt = Exemptions.ContainsKey($"{setting} {rule}");
            if (problem.Length > 0 && !exempt)
            {
                problems.Add($"{setting}: {rule} {problem}");
            }
            else if (problem.Length == 0 && exempt)
            {
                problems.Add($"{setting}: {rule} passes now, remove its exemption");
            }
        }

        return problems;
    }

    internal sealed record TestMethod(string Name, string Body);

    private sealed record Sources(string Readers, string Writers, string Scripts, string Styles, IReadOnlyList<TestMethod> Tests, IReadOnlyList<(string Path, string Text)> AllFiles, IReadOnlyList<(string Path, string Text)> ReaderFiles)
    {
        public static Sources Load()
        {
            var root = RepoPaths.Root();
            var plugin = Path.Combine(root, PluginDir);
            var files = new[] { "*.cs", "*.js" }
                .SelectMany(pattern => Directory.GetFiles(plugin, pattern, SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(f => (Path: Path.GetRelativePath(plugin, f).Replace('\\', '/'), Text: StripComments(File.ReadAllText(f))))
                .ToList();
            var declaring = new[] { "Configuration/PluginConfiguration.cs", "Configuration/CacheConfig.cs", "Configuration/ModulesConfig.cs", "Widgets/Core/WidgetConfig.cs", "Configuration/UserConfiguration.cs" };

            var readers = files.Where(f => !f.Path.StartsWith("Configuration/", StringComparison.Ordinal)
                && f.Path != "Plugin.cs" && f.Path != "Web/config.js" && !declaring.Contains(f.Path));
            var writers = files.Where(f => f.Path != "Plugin.cs" && !declaring.Contains(f.Path));

            var testsDir = Path.Combine(root, "src", "Jellyfin.Plugin.JuxHomepage.Tests");
            // A test is a [Fact] or [Theory] method, not a public helper; its body runs up to the next test
            // (A-07).
            var tests = Directory.GetFiles(testsDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith($"{nameof(ConfigurationEffectTests)}.cs", StringComparison.Ordinal))
                .SelectMany(f => TestMethods(File.ReadAllText(f)))
                .ToList();

            var scripts = files.Where(f => f.Path.EndsWith(".js", StringComparison.Ordinal));
            var styles = Directory.GetFiles(Path.Combine(plugin, "Web"), "*.css")
                .Select(f => Regex.Replace(File.ReadAllText(f), @"/\*[\s\S]*?\*/", string.Empty));

            return new Sources(
                string.Join('\n', readers.Select(f => f.Text)),
                string.Join('\n', writers.Select(f => f.Text)),
                string.Join('\n', scripts.Select(f => f.Text)),
                string.Join('\n', styles),
                tests,
                files,
                readers.ToList());
        }

        /// <summary>The data-jux-* attributes the scripts set, by setAttribute or a dataset assignment.</summary>
        internal static string[] SetAttributes(string scripts) => Regex.Matches(scripts, @"setAttribute\('(data-jux-[a-z-]+)'|dataset\.(jux\w+)\s*=(?!=)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : DatasetAttribute(m.Groups[2].Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        /// <summary>Whether a selector, getAttribute/hasAttribute or a dataset read (not an assignment) reads the attribute.</summary>
        internal static bool ReadsAttribute(string code, string attribute)
        {
            var dataset = Regex.Replace(attribute["data-".Length..], "-([a-z])", m => m.Groups[1].Value.ToUpperInvariant());
            return Regex.IsMatch(code, $@"\[{Regex.Escape(attribute)}\b|(?:get|has)Attribute\('{Regex.Escape(attribute)}'\)|dataset\.{dataset}\b(?!\s*=(?!=))");
        }

        private static string DatasetAttribute(string key) => "data-" + Regex.Replace(key, "[A-Z]", m => "-" + m.Value.ToLowerInvariant());

        internal static IEnumerable<TestMethod> TestMethods(string source)
        {
            source = StripComments(source);
            var matches = Regex.Matches(source, @"\[(?:Fact|Theory)\b[^\]]*\](?:\s*\[[^\]]*\])*\s*public (?:async Task|void) (\w+)\(");
            for (var i = 0; i < matches.Count; i++)
            {
                var end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
                yield return new TestMethod(matches[i].Groups[1].Value, source[matches[i].Index..end]);
            }
        }

        private static string StripComments(string text) => Regex.Replace(text, @"//[^\n]*|/\*[\s\S]*?\*/", string.Empty);
    }
}
