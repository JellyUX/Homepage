using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// One widget row rendered before it is saved, as returned by <c>POST JuxHomepage/Preview</c>
/// (TODO_V4.md 6.5, F-62).
/// </summary>
/// <param name="DisplayName">The section title the home screen would show.</param>
/// <param name="Items">The items the section would show, up to the row's MaxItems.</param>
/// <param name="TotalRecordCount">How many items the row finds.</param>
/// <param name="MinItems">The row's MinItems, after validation.</param>
/// <param name="HiddenReason">Why the home screen would hide the section, or null when it shows it:
/// <see cref="HiddenBelowMinItems"/> or <see cref="HiddenNoValue"/>.</param>
public sealed record WidgetPreview(
    string DisplayName,
    IReadOnlyList<BaseItemDto> Items,
    int TotalRecordCount,
    int MinItems,
    string? HiddenReason)
{
    /// <summary>The row finds fewer items than its MinItems.</summary>
    public const string HiddenBelowMinItems = "MinItems";

    /// <summary>The row resolves to no section for this user (e.g. a personalized row with no scored value).</summary>
    public const string HiddenNoValue = "NoValue";
}
