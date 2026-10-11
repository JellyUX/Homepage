using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// The plugin calls <c>IUserManager.GetUsers()</c>, which replaced the <c>Users</c> property in
/// Jellyfin 10.11.9. Every published version that contains that call (2.4.0.0 onwards) and the next
/// build must declare at least that ABI, so the catalog never offers them to an older server.
/// </summary>
public sealed class ManifestTargetAbiTests
{
    private static readonly Version MinimumAbi = new(10, 11, 9, 0);
    private static readonly Version FirstVersionRequiringIt = new(2, 4, 0, 0);

    [Fact]
    public void BuildYaml_TargetAbi_IsAtLeastTheRequiredMinimum()
    {
        var buildYaml = File.ReadAllText(Path.Combine(RepoPaths.Root(), "build.yaml"));
        var match = Regex.Match(buildYaml, @"^targetAbi:\s*""([0-9.]+)""", RegexOptions.Multiline);

        Assert.True(match.Success, "build.yaml has no targetAbi line.");
        Assert.True(Version.Parse(match.Groups[1].Value) >= MinimumAbi, $"build.yaml targetAbi {match.Groups[1].Value} < {MinimumAbi}");
    }

    [Fact]
    public void Manifest_EveryVersionCallingGetUsers_DeclaresTheRequiredMinimum()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "manifest.json")));

        var offending = manifest.RootElement[0].GetProperty("versions").EnumerateArray()
            .Where(v => Version.Parse(v.GetProperty("version").GetString()!) >= FirstVersionRequiringIt)
            .Where(v => Version.Parse(v.GetProperty("targetAbi").GetString()!) < MinimumAbi)
            .Select(v => v.GetProperty("version").GetString())
            .ToList();

        Assert.Empty(offending);
    }
}
