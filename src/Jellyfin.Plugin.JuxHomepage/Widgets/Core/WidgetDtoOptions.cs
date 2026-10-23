using MediaBrowser.Controller.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Shared DtoOptions builder for widget queries. Requests primary image aspect ratio, creation
/// date, thumbnail, and backdrop images -- previously duplicated identically across all four widget
/// base classes.
/// </summary>
public static class WidgetDtoOptions
{
    /// <summary>Builds the standard <see cref="DtoOptions"/> used by every widget category.</summary>
    /// <returns>A pre-configured <see cref="DtoOptions"/> instance.</returns>
    public static DtoOptions Standard() => new()
    {
        Fields =
        [
            ItemFields.PrimaryImageAspectRatio,
            ItemFields.DateCreated
        ],
        ImageTypeLimit = 1,
        ImageTypes =
        [
            ImageType.Primary,
            ImageType.Thumb,
            ImageType.Backdrop
        ]
    };

    /// <summary>
    /// Builds the <see cref="DtoOptions"/> of a featured section (KTW-01): the standard fields plus
    /// the overview, taglines and genres of each slide, and its backdrop, logo and poster.
    /// </summary>
    /// <returns>A pre-configured <see cref="DtoOptions"/> instance.</returns>
    public static DtoOptions Featured() => new()
    {
        Fields =
        [
            ItemFields.PrimaryImageAspectRatio,
            ItemFields.DateCreated,
            ItemFields.Overview,
            ItemFields.Taglines,
            ItemFields.Genres
        ],
        ImageTypeLimit = 1,
        ImageTypes =
        [
            ImageType.Primary,
            ImageType.Backdrop,
            ImageType.Logo
        ]
    };
}
