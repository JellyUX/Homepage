using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Controllers;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Controllers;

public sealed class JuxUserSettingsControllerTests
{
    private readonly User _caller = TestMocks.DefaultUser();
    private readonly Mock<IUserConfigurationStore> _store = new();

    [Fact]
    public async Task Save_StoresTheCallersSettings_AndAnswersNoContent()
    {
        var saved = new UserConfiguration { UserId = _caller.Id };
        _store.Setup(s => s.UpdateUserConfiguration(_caller.Id, It.IsAny<Func<UserConfiguration, bool>>()))
            .Returns((Guid _, Func<UserConfiguration, bool> change) => change(saved));

        var result = await BuildController().Save(Guid.Empty, new UserSettingsUpdate { Enabled = false });

        Assert.IsType<NoContentResult>(result);
        Assert.False(saved.Enabled);
    }

    [Fact]
    public async Task Save_WhenTheSettingsCannotBeWritten_Answers500()
    {
        _store.Setup(s => s.UpdateUserConfiguration(It.IsAny<Guid>(), It.IsAny<Func<UserConfiguration, bool>>())).Returns(false);

        var result = await BuildController().Save(Guid.Empty, new UserSettingsUpdate { Enabled = false });

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public async Task Save_AnotherUsersSettings_IsForbiddenToARegularUser()
    {
        var result = await BuildController().Save(Guid.NewGuid(), new UserSettingsUpdate { Enabled = false });

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _store.Verify(s => s.UpdateUserConfiguration(It.IsAny<Guid>(), It.IsAny<Func<UserConfiguration, bool>>()), Times.Never);
    }

    [Fact]
    public async Task Get_ReturnsTheCallersSettings()
    {
        _store.Setup(s => s.GetUserConfiguration(_caller.Id)).Returns(new UserConfiguration { UserId = _caller.Id, Enabled = false });

        var result = await BuildController().Get(Guid.Empty, "en");

        var view = Assert.IsType<UserSettingsView>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(view.Enabled);
    }

    [Fact]
    public async Task Get_AnotherUsersSettings_IsForbiddenToARegularUser()
    {
        var result = await BuildController().Get(Guid.NewGuid(), "en");

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task Get_AnotherUsersSettings_IsServedToAnAdministrator()
    {
        var other = Guid.NewGuid();
        _store.Setup(s => s.GetUserConfiguration(other)).Returns(new UserConfiguration { UserId = other, Enabled = false });

        var result = await BuildController(isAdministrator: true).Get(other, "en");

        Assert.False(Assert.IsType<UserSettingsView>(Assert.IsType<OkObjectResult>(result.Result).Value).Enabled);
    }

    [Fact]
    public async Task Save_WithAnApiKey_MustNameAUser()
    {
        _store.Setup(s => s.UpdateUserConfiguration(It.IsAny<Guid>(), It.IsAny<Func<UserConfiguration, bool>>())).Returns(true);
        var other = Guid.NewGuid();

        var withoutUser = await BuildController(apiKey: true).Save(Guid.Empty, new UserSettingsUpdate { Enabled = false });
        var namingAUser = await BuildController(apiKey: true).Save(other, new UserSettingsUpdate { Enabled = false });

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(withoutUser).StatusCode);
        Assert.IsType<NoContentResult>(namingAUser);
        _store.Verify(s => s.UpdateUserConfiguration(other, It.IsAny<Func<UserConfiguration, bool>>()), Times.Once);
    }

    private JuxUserSettingsController BuildController(bool isAdministrator = false, bool apiKey = false)
    {
        var caller = apiKey ? null : _caller;

        var localization = new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>());
        Func<PluginConfiguration?> configuration = () => new PluginConfiguration();
        var resolver = new WidgetLayoutResolver(new WidgetRegistry(), _store.Object, localization, configuration, NullLogger<WidgetLayoutResolver>.Instance);
        var widgetService = new WidgetService(new WidgetRegistry(), new SessionCache(), resolver, configuration, NullLogger<WidgetService>.Instance);
        var service = new UserSettingsService(_store.Object, localization, widgetService, configuration);

        return new JuxUserSettingsController(service, TestCaller.AuthContext(caller).Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = TestCaller.Principal(caller, isAdministrator) }
            }
        };
    }
}
