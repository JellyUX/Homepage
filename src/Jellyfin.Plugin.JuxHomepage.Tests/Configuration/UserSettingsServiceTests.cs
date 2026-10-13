using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Configuration;

/// <summary>
/// A user's own settings (TODO_V4.md 8.3, F-36): hide a home section, turn JellyUX off for oneself.
/// </summary>
public sealed class UserSettingsServiceTests
{
    private const string GenreType = "jux.personalized.favorite-genre";
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _seasonalId = Guid.NewGuid().ToString();
    private readonly WidgetConfig _shows = new() { WidgetType = "jux.native.recently-added-shows", Order = 10, AllowUserOverride = true, MinItems = 0 };
    private readonly WidgetConfig _genre1 = new() { WidgetType = GenreType, Order = 20, AllowUserOverride = true, MinItems = 0 };
    private readonly WidgetConfig _genre2 = new() { WidgetType = GenreType, Order = 30, AllowUserOverride = true, MinItems = 0 };
    private readonly WidgetConfig _adminGenre = new() { WidgetType = "jux.admin.genre", Order = 40, AllowUserOverride = false, CustomDisplayName = "Aventure", MinItems = 0 };
    private readonly WidgetConfig _seasonal;
    private readonly WidgetConfig _disabled = new() { WidgetType = "jux.native.next-up", Order = 60, Enabled = false, AllowUserOverride = true };
    private readonly Mock<IUserConfigurationStore> _store = new();
    private UserConfiguration? _saved;

    public UserSettingsServiceTests()
    {
        _seasonal = new WidgetConfig
        {
            WidgetType = "jux.connected.seasonal",
            Order = 50,
            AllowUserOverride = true,
            CustomDisplayName = "Halloween",
            ExtraParams = [new WidgetExtraParam { Key = "value", Value = _seasonalId }]
        };
        _store.Setup(s => s.GetUserConfiguration(_userId)).Returns(() => _saved);
        _store.Setup(s => s.SaveUserConfiguration(It.IsAny<UserConfiguration>())).Callback<UserConfiguration>(c => _saved = c);
    }

    private WidgetConfig[] Rows => [_shows, _genre1, _genre2, _adminGenre, _seasonal, _disabled];

    [Fact]
    public void SectionKeys_UseTheInstanceIdOrTheTypeAndRank()
    {
        var keys = WidgetLayoutResolver.SectionKeys(Rows);

        Assert.Equal("jux.native.recently-added-shows#1", keys[_shows]);
        Assert.Equal(GenreType + "#2", keys[_genre2]);
        Assert.Equal(_seasonalId, keys[_seasonal]);
    }

    [Fact]
    public void Get_ListsEveryEnabledSection_NumberingSharedNames()
    {
        var service = BuildService(new SessionCache());

        var view = service.Get(_userId, "en");

        Assert.True(view.Enabled);
        Assert.Equal(["Recently Added Shows", "Favorite Genre", "Favorite Genre (2)", "Aventure", "Halloween"], view.Sections.Select(s => s.Name));
        Assert.All(view.Sections, s => Assert.False(s.Hidden));
    }

    [Fact]
    public void Save_KeepsOnlyKnownKeys_StampsTheSchemaAndDropsTheCachedLayout()
    {
        using var cache = new SessionCache();
        cache.Set(_userId, "en", []);
        var service = BuildService(cache);

        service.Save(_userId, new UserSettingsUpdate { Enabled = false, HiddenSections = [GenreType + "#2", "jux.admin.genre#1", "bogus"] });

        Assert.Equal([GenreType + "#2", "jux.admin.genre#1"], _saved!.HiddenSections);
        Assert.False(_saved.Enabled);
        Assert.Equal(_userId, _saved.UserId);
        Assert.Equal(10, _saved.SchemaVersion);
        Assert.False(cache.TryGet(_userId, "en", TimeSpan.FromMinutes(5), out _));
        Assert.True(service.Get(_userId, "en").Sections.Single(s => s.Name == "Favorite Genre (2)").Hidden);
    }

    [Fact]
    public async Task HiddenSection_LeavesThatUsersLayoutOnly()
    {
        var registry = new WidgetRegistry();
        foreach (var type in new[] { _shows.WidgetType, GenreType })
        {
            var widget = new Mock<IWidget>();
            widget.SetupGet(w => w.WidgetType).Returns(type);
            widget.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>())).Returns(widget.Object);
            widget.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = type });
            widget.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WidgetResult([], 0));
            registry.Register(widget.Object);
        }

        _saved = new UserConfiguration { UserId = _userId, HiddenSections = ["jux.native.recently-added-shows#1"] };
        var resolver = new WidgetLayoutResolver(
            registry,
            _store.Object,
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => new PluginConfiguration { Widgets = [_shows, _genre1] },
            NullLogger<WidgetLayoutResolver>.Instance);

        var mine = await resolver.BuildDescriptors(_userId, null, CancellationToken.None);
        var admin = await resolver.BuildDescriptors(Guid.NewGuid(), null, CancellationToken.None);

        Assert.Equal([GenreType], mine.Select(d => d.WidgetType));
        Assert.Equal([_shows.WidgetType, GenreType], admin.Select(d => d.WidgetType));
    }

    private UserSettingsService BuildService(SessionCache cache)
    {
        var localization = new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string>
            {
                [_shows.WidgetType] = "Recently Added Shows",
                [GenreType] = "Favorite Genre"
            }
        });
        var resolver = new WidgetLayoutResolver(new WidgetRegistry(), _store.Object, localization, () => new PluginConfiguration { Widgets = Rows }, NullLogger<WidgetLayoutResolver>.Instance);
        var widgetService = new WidgetService(new WidgetRegistry(), cache, resolver, () => new PluginConfiguration(), NullLogger<WidgetService>.Instance);
        return new UserSettingsService(_store.Object, localization, widgetService, () => new PluginConfiguration { Widgets = Rows });
    }
}
