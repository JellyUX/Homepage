using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// Test classes that assign Jellyfin's static <c>BaseItem.LibraryManager</c> join this collection so
/// they never run in parallel and overwrite each other's mock.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BaseItemStaticCollection
{
    public const string Name = "BaseItem static services";
}
