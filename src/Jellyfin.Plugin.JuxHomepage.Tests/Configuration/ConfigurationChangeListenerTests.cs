using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Configuration;

public sealed class ConfigurationChangeListenerTests
{
    [Fact]
    public void OnConfigurationChanged_ClearsLayoutsAndScoringSnapshots()
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);
        var film = new Movie { Id = Guid.NewGuid(), Name = "Film", Genres = ["Drama"] };
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) =>
                q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie) ? [film] : []);
        libraryManagerMock.Setup(m => m.GetPeople(It.IsAny<BaseItem>())).Returns([]);
        using var scoring = new ScoringService(
            userManagerMock.Object,
            libraryManagerMock.Object,
            new Mock<IUserDataManager>().Object,
            () => new PluginConfiguration());
        using var sessionCache = new SessionCache();
        sessionCache.Set(user.Id, "en", [new WidgetDescriptor { WidgetType = "w1" }]);
        scoring.GetTopGenres(user.Id, 5);

        new ConfigurationChangeListener(sessionCache, scoring, NullLogger<ConfigurationChangeListener>.Instance, Backup()).OnConfigurationChanged(null);

        Assert.False(sessionCache.TryGet(user.Id, "en", TimeSpan.FromMinutes(15), out _, out _));
        Assert.False(scoring.HasSnapshotForTesting(user.Id));
    }

    [Fact]
    public void OnConfigurationChanged_WritesTheCopyOfTheSavedConfiguration_AndClearsAPendingReset()
    {
        var backup = Backup();
        backup.Save(new PluginConfiguration { SchemaVersion = 11 });
        backup.MarkResetIfBackupExists();
        using var sessionCache = new SessionCache();
        using var scoring = new ScoringService(new Mock<IUserManager>().Object, new Mock<ILibraryManager>().Object, new Mock<IUserDataManager>().Object, () => new PluginConfiguration());
        var saved = new PluginConfiguration { SchemaVersion = 11, Widgets = [new WidgetConfig { WidgetType = "jux.admin.genre" }] };

        new ConfigurationChangeListener(sessionCache, scoring, NullLogger<ConfigurationChangeListener>.Instance, backup).OnConfigurationChanged(saved);

        Assert.Equal("jux.admin.genre", Assert.Single(backup.Load()!.Widgets).WidgetType);
        Assert.False(backup.IsResetPending);
    }

    private static ConfigurationBackup Backup() =>
        new(Path.Combine(Path.GetTempPath(), "jux-listener-tests-" + Guid.NewGuid()), NullLogger.Instance);
}
