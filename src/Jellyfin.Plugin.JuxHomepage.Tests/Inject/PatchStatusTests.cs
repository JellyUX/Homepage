using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Inject;

// FIAB-04: the startup dry run records what the client's stale chunk guard asks for before a reload.
public sealed class PatchStatusTests
{
    private const string FragmentTemplate = "/*{{cardbuilder}}{{this_hook}}*/";
    private const string PatchableChunk = "var u=r(1);var n={A:function(){return u.default}};,loadSections:function(){},other:2";
    private const string DriftedChunk = "someExpression.without.a.var.decl,loadSections:function(){},other:2";

    [Fact]
    public void Combine_IsPatchedWhenOneChunkIs_DriftedWhenNoneIs_AndNoChunkWhenNoneWasFound()
    {
        Assert.Equal(LoadSectionsPatchState.Patched, PatchStatus.Combine([LoadSectionsOutcome.HookNotFound, LoadSectionsOutcome.Patched]));
        Assert.Equal(LoadSectionsPatchState.HookNotFound, PatchStatus.Combine([LoadSectionsOutcome.HookNotFound]));
        Assert.Equal(LoadSectionsPatchState.NoChunk, PatchStatus.Combine([]));
    }

    [Theory]
    [InlineData(PatchableChunk, true)]
    [InlineData(DriftedChunk, false)]
    public void DryRun_ReturnsTheOutcomeOfTheChunkOnDisk(string chunk, bool patched)
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.ReadAllText("56213.abc.chunk.js")).Returns(chunk);

        var outcome = StartupService.WarnIfLoadSectionsHookHasDrifted("56213.abc.chunk.js", "56213.abc.chunk.js", FragmentTemplate, fileSystem.Object, NullLogger.Instance);

        Assert.Equal(patched ? LoadSectionsOutcome.Patched : LoadSectionsOutcome.HookNotFound, outcome);
    }

    [Fact]
    public void DryRun_UnreadableChunkOrMissingFragment_KnowsNothing()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.ReadAllText(It.IsAny<string>())).Throws(new IOException("locked"));

        Assert.Null(StartupService.WarnIfLoadSectionsHookHasDrifted("c.js", "c.js", FragmentTemplate, fileSystem.Object, NullLogger.Instance));
        Assert.Null(StartupService.WarnIfLoadSectionsHookHasDrifted("c.js", "c.js", null, fileSystem.Object, NullLogger.Instance));
    }
}
