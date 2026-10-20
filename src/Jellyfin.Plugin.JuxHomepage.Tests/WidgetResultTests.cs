using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Dto;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// PERF-03.a: a widget page is projected to DTOs on its first read, so the layout probe, which only
/// counts the page, builds none.
/// </summary>
public sealed class WidgetResultTests
{
    [Fact]
    public void Probe_BuildsNoDto_AndTheFirstReadOfItemsBuildsThemOnce()
    {
        var calls = 0;
        var dto = new Mock<IDtoService>();
        dto.Setup(d => d.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns((IReadOnlyList<BaseItem> items, DtoOptions _, User _, BaseItem _) =>
            {
                calls++;
                return [.. items.Select(i => new BaseItemDto { Id = i.Id })];
            });
        BaseItem[] page = [new Movie { Id = Guid.NewGuid() }, new Movie { Id = Guid.NewGuid() }];

        var result = WidgetResult.Project(dto.Object, page, new DtoOptions(), TestMocks.DefaultUser(), totalRecordCount: 9);

        Assert.Equal((2, 9, 0), (result.ItemCount, result.TotalRecordCount, calls));
        Assert.Equal(page.Select(i => i.Id), result.Items.Select(i => i.Id));
        Assert.Same(result.Items, result.Items);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FailedProjection_IsNotKept_TheNextReadProjectsAgain()
    {
        var attempts = 0;
        var result = WidgetResult.Defer(1, 1, () => ++attempts == 1 ? throw new InvalidOperationException("dto") : [new BaseItemDto()]);

        Assert.Throws<InvalidOperationException>(() => result.Items);
        Assert.Single(result.Items);
        Assert.Equal(2, attempts);
    }
}
