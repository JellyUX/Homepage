using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

// QUAL-06.a, C# side of the contract test/extraparams-contract.test.js checks on the admin page: the
// ExtraParams keys of a widget type are the "*Param" constants of its class and of its base classes.
// A key renamed on one side only turns one of the two tests red.
public sealed class ExtraParamsContractTests
{
    private static readonly Lazy<Dictionary<string, string[]>> Contract = new(() =>
    {
        var path = Path.Combine(RepoPaths.Root(), "test", "fixtures", "extraparams-contract.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("widgets").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(k => k.GetString()!).Order(StringComparer.Ordinal).ToArray());
    });

    public static TheoryData<string> AllWidgetTypes() => new(WidgetTypes.All.Keys.Order(StringComparer.Ordinal));

    [Fact]
    public void Contract_ListsEveryRegisteredWidgetType()
    {
        Assert.Equal(WidgetTypes.All.Keys.Order(StringComparer.Ordinal), Contract.Value.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllWidgetTypes))]
    public void WidgetParamConstants_MatchTheContract(string widgetType)
    {
        var keys = WidgetTypes.All[widgetType]
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.Name.EndsWith("Param", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(Contract.Value[widgetType], keys);
    }

    // A key written as a literal instead of its constant escapes the contract: no plugin source line may
    // quote a contract key, except where the constant itself is declared.
    [Fact]
    public void PluginSource_QuotesNoContractKeyOutsideItsConstant()
    {
        var keys = Contract.Value.Values.SelectMany(k => k).Distinct();
        var literal = new Regex("\"(" + string.Join('|', keys.Select(Regex.Escape)) + ")\"", RegexOptions.Compiled);
        var plugin = Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage");
        var offenders = Directory.GetFiles(plugin, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line.Trim())))
            .Where(l => !l.Text.StartsWith("//", StringComparison.Ordinal)
                && !l.Text.Contains("const string", StringComparison.Ordinal)
                && !l.Text.Contains("JsonPropertyName", StringComparison.Ordinal)
                && literal.IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}: {l.Text}")
            .ToList();

        Assert.Empty(offenders);
    }
}
