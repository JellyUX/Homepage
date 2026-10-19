using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;

namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// The settings a user changes for themselves on the JellyUX block of the native Home preferences
/// page (TODO_V4.md 8.3, F-36): turn JellyUX off for themselves, and hide home sections. Writes
/// <see cref="UserConfiguration"/> through <see cref="IUserConfigurationStore"/>.
/// </summary>
public sealed class UserSettingsService
{
    private readonly IUserConfigurationStore _store;
    private readonly ILocalizationService _localizationService;
    private readonly WidgetService _widgetService;
    private readonly Func<PluginConfiguration?> _getConfiguration;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserSettingsService"/> class.
    /// </summary>
    /// <param name="store">The per-user configuration store.</param>
    /// <param name="localizationService">Widget display-name translation service.</param>
    /// <param name="widgetService">The widget engine, whose cached layout of the user is dropped on save.</param>
    /// <param name="getConfiguration">Returns the current plugin configuration.</param>
    public UserSettingsService(
        IUserConfigurationStore store,
        ILocalizationService localizationService,
        WidgetService widgetService,
        Func<PluginConfiguration?> getConfiguration)
    {
        _store = store;
        _localizationService = localizationService;
        _widgetService = widgetService;
        _getConfiguration = getConfiguration;
    }

    /// <summary>
    /// Returns the user's settings: whether JellyUX runs for them, and every home section the
    /// administrator enabled, which they may hide, in home screen order. Hiding changes none of the
    /// section's settings, so it does not depend on <see cref="WidgetConfig.AllowUserOverride"/>.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="lang">The language of the section names.</param>
    /// <returns>The user's settings.</returns>
    public UserSettingsView Get(Guid userId, string? lang)
    {
        var userConfig = _store.GetUserConfiguration(userId);
        var hidden = new HashSet<string>(userConfig?.HiddenSections ?? [], StringComparer.Ordinal);
        var rows = _getConfiguration()?.Widgets ?? [];
        var keys = WidgetLayoutResolver.SectionKeys(rows);
        var normalizedLang = _localizationService.NormalizeLanguage(lang);

        var sections = new List<UserSectionSetting>();
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows.Where(r => r.Enabled).OrderBy(r => r.Order))
        {
            // Rows without a custom name share their type's name (two Favorite Genre rows): a number
            // tells them apart, in home screen order.
            var name = row.CustomDisplayName ?? _localizationService.Translate(row.WidgetType, normalizedLang);
            nameCounts[name] = nameCounts.GetValueOrDefault(name) + 1;
            var shown = nameCounts[name] == 1 ? name : $"{name} ({nameCounts[name]})";
            sections.Add(new UserSectionSetting(keys[row], shown, hidden.Contains(keys[row])));
        }

        return new UserSettingsView(userConfig?.Enabled != false, sections);
    }

    /// <summary>
    /// Saves the user's settings. Keys that name no configured section are dropped.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="update">The settings to save.</param>
    /// <returns>False when the settings could not be written; the cached layout is then kept.</returns>
    public bool Save(Guid userId, UserSettingsUpdate update)
    {
        var rows = _getConfiguration()?.Widgets ?? [];
        var keys = WidgetLayoutResolver.SectionKeys(rows);
        var hideable = keys.Values.ToHashSet(StringComparer.Ordinal);

        var config = _store.GetUserConfiguration(userId) ?? new UserConfiguration { UserId = userId };
        config.UserId = userId;
        config.Enabled = update.Enabled ? null : false;
        config.HiddenSections = (update.HiddenSections ?? []).Where(hideable.Contains).Distinct(StringComparer.Ordinal).ToArray();
        Plugin.MigrateUserConfiguration(config);
        if (!_store.SaveUserConfiguration(config))
        {
            return false;
        }

        _widgetService.InvalidateUser(userId);
        return true;
    }
}
