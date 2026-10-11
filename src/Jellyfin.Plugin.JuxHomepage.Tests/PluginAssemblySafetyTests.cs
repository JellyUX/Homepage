using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// The plugin must never load code from disk at runtime: the external widget-pack loader was removed
/// (TODO_V4.md F-70) because it executed any DLL dropped in a data folder inside Jellyfin's process.
/// </summary>
public sealed class PluginAssemblySafetyTests
{
    [Fact]
    public void PluginAssembly_NeverLoadsAssembliesFromDisk()
    {
        using var stream = File.OpenRead(typeof(Plugin).Assembly.Location);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        var diskLoads = metadata.MemberReferences
            .Select(handle => metadata.GetMemberReference(handle))
            .Select(member => (Type: TypeName(metadata, member.Parent), Name: metadata.GetString(member.Name)))
            .Where(call =>
                (call.Type == "System.Reflection.Assembly" && call.Name is "LoadFile" or "LoadFrom" or "UnsafeLoadFrom")
                || (call.Type == "System.Runtime.Loader.AssemblyLoadContext" && call.Name.StartsWith("LoadFrom", StringComparison.Ordinal)))
            .Select(call => $"{call.Type}.{call.Name}")
            .ToList();

        Assert.Empty(diskLoads);
    }

    private static string TypeName(MetadataReader metadata, EntityHandle parent)
    {
        if (parent.Kind != HandleKind.TypeReference)
        {
            return string.Empty;
        }

        var type = metadata.GetTypeReference((TypeReferenceHandle)parent);
        return $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
    }
}
