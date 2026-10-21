using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Configuration;

/// <summary>
/// PERF-07, DEC-P07-10: the language of each user's last home screen is kept in their settings file,
/// written only when it changes and without touching their other settings.
/// </summary>
public sealed class HomeLanguageMemoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-home-language-tests-" + Guid.NewGuid());
    private readonly UserConfigurationStore _store;
    private readonly Guid _userId = Guid.NewGuid();

    public HomeLanguageMemoryTests()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.DataPath).Returns(_tempDir);
        _store = new UserConfigurationStore(paths.Object, new FailingFileSystem(), NullLogger<UserConfigurationStore>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void HomeLanguage_FirstHomeScreen_IsWrittenAndRead()
    {
        var memory = new HomeLanguageMemory(_store);
        Assert.Null(memory.Get(_userId));

        memory.Remember(_userId, "fr");

        Assert.Equal("fr", memory.Get(_userId));
        using var reopened = Reopened();
        Assert.Equal("fr", reopened.GetUserConfiguration(_userId)?.HomeLanguage);
    }

    [Fact]
    public void HomeLanguage_SameLanguage_WritesNothing_AnotherOneIsWritten()
    {
        var store = new Mock<IUserConfigurationStore>();
        store.Setup(s => s.GetUserConfiguration(_userId)).Returns(new UserConfiguration { UserId = _userId, HomeLanguage = "fr" });
        var memory = new HomeLanguageMemory(store.Object);

        memory.Remember(_userId, "fr");
        store.Verify(s => s.UpdateUserConfiguration(It.IsAny<Guid>(), It.IsAny<Func<UserConfiguration, bool>>()), Times.Never);

        memory.Remember(_userId, "de");
        store.Verify(s => s.UpdateUserConfiguration(_userId, It.IsAny<Func<UserConfiguration, bool>>()), Times.Once);
    }

    [Fact]
    public void HomeLanguage_KeepsTheUsersOtherSettings()
    {
        _store.SaveUserConfiguration(new UserConfiguration { UserId = _userId, Enabled = false, HiddenSections = ["jux.admin.genre#1"] });

        new HomeLanguageMemory(_store).Remember(_userId, "fr");

        using var reopened = Reopened();
        var config = reopened.GetUserConfiguration(_userId)!;
        Assert.Equal(("fr", false, "jux.admin.genre#1"), (config.HomeLanguage, config.Enabled, Assert.Single(config.HiddenSections)));
    }

    [Fact]
    public void UpdateUserConfiguration_ChangeWithNothingToSave_WritesNoFile()
    {
        Assert.True(_store.UpdateUserConfiguration(_userId, _ => false));

        using var reopened = Reopened();
        Assert.Null(reopened.GetUserConfiguration(_userId));
    }

    /// <summary>A store over the same directory with nothing in memory: what a restart reads.</summary>
    private UserConfigurationStore Reopened()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.DataPath).Returns(_tempDir);
        return new UserConfigurationStore(paths.Object, new FailingFileSystem(), NullLogger<UserConfigurationStore>.Instance);
    }
}
