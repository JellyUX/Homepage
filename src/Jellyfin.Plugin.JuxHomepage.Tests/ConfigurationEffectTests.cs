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
/// (the admin or user settings page counts); (c) a test is named after it. Read from the sources, so a
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

    // Settings with no effect today, each fixed by a later phase: checked by the skipped facts below.
    private static readonly string[] Deferred =
        ["WidgetConfig.MinInstances", "WidgetConfig.MaxInstances", "UserConfiguration.WidgetOverrides"];

    private static readonly Lazy<Sources> Code = new(Sources.Load);

    public static TheoryData<string> Settings()
    {
        var data = new TheoryData<string>();
        foreach (var setting in ConfigurationTypes.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                     .Where(p => p.CanWrite)
                     .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
                     .Where(s => !Deferred.Contains(s)))
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

    [Fact(Skip = "QUAL-02, phase 09: MinInstances is read by the migrations only, no section count depends on it.")]
    public void MinInstances_IsReadWrittenAndTested() => Assert.Empty(Problems("WidgetConfig.MinInstances"));

    [Fact(Skip = "QUAL-02, phase 09: MaxInstances is read by the migrations only (the old fan-out), no section count depends on it.")]
    public void MaxInstances_IsReadWrittenAndTested() => Assert.Empty(Problems("WidgetConfig.MaxInstances"));

    [Fact(Skip = "QUAL-02, phase 09: no page writes the per-user WidgetOverrides, only the migrations do.")]
    public void WidgetOverrides_IsReadWrittenAndTested() => Assert.Empty(Problems("UserConfiguration.WidgetOverrides"));

    private static List<string> Problems(string setting)
    {
        var name = setting.Split('.')[1];
        var failed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["(a)"] = Regex.IsMatch(Code.Value.Readers, $@"\.{name}\b") ? string.Empty : "no production code outside the configuration reads it",
            ["(b)"] = Regex.IsMatch(Code.Value.Writers, $@"(\.|\b){name}\s*(=(?!=)|:)") ? string.Empty : "no production code outside the migrations writes it",
            ["(c)"] = Code.Value.TestNames.Any(test => test.Contains(name, StringComparison.Ordinal)) ? string.Empty : "no test is named after it",
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

    private sealed record Sources(string Readers, string Writers, IReadOnlyList<string> TestNames)
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
            var testNames = Directory.GetFiles(testsDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith($"{nameof(ConfigurationEffectTests)}.cs", StringComparison.Ordinal))
                .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"public (?:async Task|void) (\w+)\(").Select(m => m.Groups[1].Value))
                .ToList();

            return new Sources(string.Join('\n', readers.Select(f => f.Text)), string.Join('\n', writers.Select(f => f.Text)), testNames);
        }

        private static string StripComments(string text) => Regex.Replace(text, @"//[^\n]*|/\*[\s\S]*?\*/", string.Empty);
    }
}
