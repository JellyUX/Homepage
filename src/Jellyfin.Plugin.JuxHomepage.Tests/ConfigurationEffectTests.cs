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

    private sealed record Sources(string Readers, string Writers, string Scripts, IReadOnlyList<TestMethod> Tests)
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

            return new Sources(
                string.Join('\n', readers.Select(f => f.Text)),
                string.Join('\n', writers.Select(f => f.Text)),
                string.Join('\n', scripts.Select(f => f.Text)),
                tests);
        }

        internal static IEnumerable<TestMethod> TestMethods(string source)
        {
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
