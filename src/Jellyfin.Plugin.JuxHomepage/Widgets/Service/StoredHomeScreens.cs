using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// The deferred level of each user's home screen kept on disk (DR-033): per language, the layout's deferred rows and
/// the ordered ids of each one, so a home screen after a restart, an eviction or an expiry is read again from the
/// stored order with live user data, without scoring nor probing a deferred row.
/// <para>
/// One file per user, <c>DataPath/Jellyfin.Plugin.JuxHomepage/cache/home/{userId}.json</c>, never under the
/// plugins tree Jellyfin scans. A stored layout is used while it is younger than the deferred freshness, its
/// discriminant matches the current configuration, and no mark taken after it was computed names its user or one of
/// its sources. Marks are dated and written to disk, so they survive a restart, and a mark taken while a layout is
/// computed is never cleared by the write that ends it. A file that cannot be read is logged and ignored: the home
/// screen is then computed again, never served empty (S1.c).
/// </para>
/// </summary>
public sealed class StoredHomeScreens
{
    /// <summary>The format of the stored files; a file of another format is computed again.</summary>
    internal const int FormatVersion = 1;

    private const string SourceMarksFile = "sources.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _directory;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StoredHomeScreens> _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, StoredHomeFile?> _files = [];
    private Dictionary<string, DateTime>? _sourceMarks;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredHomeScreens"/> class.
    /// </summary>
    /// <param name="dataFolder">The plugin's folder under <c>DataPath</c>.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="timeProvider">Time source of the computation dates and of the marks.</param>
    /// <param name="logger">Logger.</param>
    public StoredHomeScreens(string dataFolder, IFileSystem fileSystem, TimeProvider timeProvider, ILogger<StoredHomeScreens> logger)
    {
        _directory = Path.Combine(dataFolder, "cache", "home");
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Gets the current time of the store's clock.</summary>
    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Returns the discriminant of a stored layout: the whole plugin configuration and the user's own settings that
    /// shape the layout (JellyUX on or off, hidden sections), so that any admin save, a row, a list filter or a module
    /// switch alike, or a change of those settings makes a layout stored before computed again. The language of the
    /// user's last home screen is left out: it changes whenever the user opens the home screen in another language,
    /// and each language has its own stored layout (R11 of phase 14).
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <param name="user">The user's own configuration, or null.</param>
    /// <returns>The discriminant.</returns>
    public static string Discriminant(PluginConfiguration? configuration, UserConfiguration? user)
    {
        var json = JsonSerializer.Serialize(new { Configuration = configuration, UserEnabled = user?.Enabled, Hidden = user?.HiddenSections });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>
    /// Returns the user's stored layout in <paramref name="lang"/> when it has the current format and discriminant,
    /// whether it is still fresh or not; null when there is none or it cannot be used.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="lang">The normalized language.</param>
    /// <param name="discriminant">The current discriminant (<see cref="Discriminant"/>).</param>
    /// <param name="freshness">How long a stored layout is served before it is computed again.</param>
    /// <returns>The stored layout and whether it is still fresh, or null.</returns>
    public (StoredLayout Layout, bool Fresh)? Read(Guid userId, string lang, string discriminant, TimeSpan freshness)
    {
        lock (_lock)
        {
            var file = ReadFile(userId);
            if (file?.Layouts is null || !file.Layouts.TryGetValue(lang, out var layout) || layout?.Rows is null)
            {
                return null;
            }

            if (layout.Discriminant != discriminant)
            {
                _logger.LogDebug("The stored {Lang} home screen of user {UserId} was computed for another configuration; computing it again.", lang, userId);
                return null;
            }

            var fresh = UtcNow - layout.ComputedAtUtc < freshness
                && layout.ComputedAtUtc > file.StaleSinceUtc
                && !layout.Rows.Any(row => row.Sources?.Any(source => SourceMarks().GetValueOrDefault(source) >= layout.ComputedAtUtc) == true);
            return (layout, fresh);
        }
    }

    /// <summary>
    /// Returns the stored ids of a section of the user, from any of their layouts computed for the current
    /// configuration (the ids do not depend on the language), fresh or not; null when none holds it.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="discriminant">The current discriminant.</param>
    /// <param name="section">The section.</param>
    /// <returns>The ordered ids, or null.</returns>
    public IReadOnlyList<Guid>? IdsOf(Guid userId, string discriminant, SectionId section)
    {
        lock (_lock)
        {
            IEnumerable<StoredLayout> layouts = ReadFile(userId)?.Layouts?.Values ?? Enumerable.Empty<StoredLayout>();
            return layouts
                .Where(layout => layout?.Rows is not null && layout.Discriminant == discriminant)
                .SelectMany(layout => layout.Rows)
                .FirstOrDefault(row => row.Section is { } stored
                    && stored.WidgetType == section.WidgetType
                    && (string.IsNullOrEmpty(stored.AdditionalData) ? null : stored.AdditionalData) == section.AdditionalData
                    && (section.SectionKey is null || row.Key == section.SectionKey))?.Ids;
        }
    }

    /// <summary>
    /// Stores the user's layout in <paramref name="lang"/>, keeping their other languages and their mark.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="lang">The normalized language.</param>
    /// <param name="layout">The layout, dated when its computation started.</param>
    public void Write(Guid userId, string lang, StoredLayout layout)
    {
        lock (_lock)
        {
            var file = ReadFile(userId) ?? new StoredHomeFile();
            file.Layouts ??= [];
            file.Layouts[lang] = layout;
            WriteUserFile(userId, file);
        }
    }

    /// <summary>
    /// Marks every stored layout of the user computed until now as due: it is still served, then computed again in
    /// the background (a new scoring). A user without a stored file yet gets one holding the mark, so a layout whose
    /// computation started before it is due once written (R11 of phase 14).
    /// </summary>
    /// <param name="userId">The user.</param>
    public void MarkUserStale(Guid userId)
    {
        lock (_lock)
        {
            var file = ReadFile(userId) ?? new StoredHomeFile();
            file.StaleSinceUtc = UtcNow;
            WriteUserFile(userId, file);
        }
    }

    /// <summary>
    /// Marks every stored row that reads one of <paramref name="sources"/>, computed until now, as due (a library
    /// scan, a refreshed TMDb or award list).
    /// </summary>
    /// <param name="sources">The changed sources.</param>
    public void MarkSourcesStale(IReadOnlyCollection<string> sources)
    {
        lock (_lock)
        {
            var marks = SourceMarks();
            var now = UtcNow;
            foreach (var source in sources)
            {
                marks[source] = now;
            }

            WriteFile(Path.Combine(_directory, SourceMarksFile), marks);
        }
    }

    /// <summary>
    /// Forgets the user's stored layouts, on disk and in memory, so the next home screen is computed as at a first visit:
    /// after a change of what the user may see, a stored section name (a title, a person) is never shown again (R11 of
    /// phase 14). A mark the user had goes with it: the next computation starts after it.
    /// </summary>
    /// <param name="userId">The user.</param>
    public void Forget(Guid userId)
    {
        lock (_lock)
        {
            _files.Remove(userId);
            if (_fileSystem.FileExists(PathOf(userId)))
            {
                _fileSystem.Delete(PathOf(userId));
            }
        }
    }

    private void WriteUserFile(Guid userId, StoredHomeFile file)
    {
        var path = PathOf(userId);
        // Kept in memory even when the disk refuses it, so the home screen is still read again until a restart.
        WriteFile(path, file);
        _files[userId] = file;
    }

    private string PathOf(Guid userId) => Path.Combine(_directory, userId.ToString("N") + ".json");

    private Dictionary<string, DateTime> SourceMarks()
    {
        if (_sourceMarks is null)
        {
            var path = Path.Combine(_directory, SourceMarksFile);
            _sourceMarks = TryRead<Dictionary<string, DateTime>>(path) ?? new Dictionary<string, DateTime>(StringComparer.Ordinal);
        }

        return _sourceMarks;
    }

    // Only the plugin writes these files: each is read from the disk once, at its first use after a start, then
    // kept in memory with the writes; a file damaged or deleted by hand counts from the next start.
    private StoredHomeFile? ReadFile(Guid userId)
    {
        if (_files.TryGetValue(userId, out var known))
        {
            return known;
        }

        var path = PathOf(userId);
        var file = TryRead<StoredHomeFile>(path);
        if (file is not null && file.Format != FormatVersion)
        {
            _logger.LogWarning("The stored home screen of user {UserId} has format {Format}, not {Expected}; computing it again.", userId, file.Format, FormatVersion);
            file = null;
        }
        else if (file is not null && !IsWhole(file))
        {
            // Valid JSON with a field left out or null (a file cut or edited by hand) would fail the layout build.
            _logger.LogWarning("The stored home screen of user {UserId} lacks a layout, a row key, ids or a section type; computing it again.", userId);
            file = null;
        }

        _files[userId] = file;
        return file;
    }

    private static bool IsWhole(StoredHomeFile file) =>
        file.Layouts?.Values.All(layout => layout?.Rows is not null
            && layout.Discriminant is not null
            && layout.Rows.All(row => row is { Key: not null, Ids: not null }
                && (row.Section is null || row.Section is { WidgetType: not null, DisplayName: not null }))) != false;

    private T? TryRead<T>(string path)
        where T : class
    {
        try
        {
            return _fileSystem.FileExists(path) ? JsonSerializer.Deserialize<T>(_fileSystem.ReadAllText(path), JsonOptions) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Could not read the stored home screen file {Path}; computing it again.", path);
            return null;
        }
    }

    private bool WriteFile<T>(string path, T contents)
    {
        var temp = path + ".tmp";
        try
        {
            _fileSystem.CreateDirectory(_directory);
            _fileSystem.WriteAllText(temp, JsonSerializer.Serialize(contents, JsonOptions));
            _fileSystem.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Could not store the home screen file {Path}; it will be computed again.", path);
            try
            {
                if (_fileSystem.FileExists(temp))
                {
                    _fileSystem.Delete(temp);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(cleanup, "Could not delete {Path}.", temp);
            }

            return false;
        }
    }

    /// <summary>The file of one user.</summary>
    internal sealed class StoredHomeFile
    {
        /// <summary>Gets or sets the format of the file.</summary>
        public int Format { get; set; } = FormatVersion;

        /// <summary>Gets or sets the date before which every layout of the user is due.</summary>
        public DateTime StaleSinceUtc { get; set; }

        /// <summary>Gets or sets the layout of each language.</summary>
        public Dictionary<string, StoredLayout>? Layouts { get; set; }
    }
}

/// <summary>The deferred rows of one user's layout in one language, as computed (DR-033).</summary>
public sealed class StoredLayout
{
    /// <summary>Gets or sets when the computation of the layout started.</summary>
    public DateTime ComputedAtUtc { get; set; }

    /// <summary>Gets or sets the configuration the layout was computed for (<see cref="StoredHomeScreens.Discriminant"/>).</summary>
    public string Discriminant { get; set; } = string.Empty;

    /// <summary>Gets or sets the deferred rows, by section key; the direct rows are always read live.</summary>
    public List<StoredRow> Rows { get; set; } = [];

    /// <summary>Returns the stored row of a section key, or null.</summary>
    /// <param name="key">The section key.</param>
    /// <returns>The row, or null.</returns>
    public StoredRow? RowOf(string key) => Rows.FirstOrDefault(row => row.Key == key);
}

/// <summary>
/// One deferred row of a stored layout: left out of the layout (<see cref="Section"/> null), or its section and the
/// ordered ids it read, with a margin beyond <c>MaxItems</c> for titles watched or removed until the next computation.
/// </summary>
public sealed class StoredRow
{
    /// <summary>Gets or sets the section key of the configuration row.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the sources the row reads, whose change makes it due.</summary>
    public List<string>? Sources { get; set; }

    /// <summary>Gets or sets the section, or null when the row was left out of the layout.</summary>
    public StoredSection? Section { get; set; }

    /// <summary>Gets or sets the ordered ids the row read.</summary>
    public List<Guid> Ids { get; set; } = [];
}

/// <summary>The fields of a <see cref="WidgetDescriptor"/> a stored row restores, the order included.</summary>
public sealed class StoredSection
{
    /// <summary>Gets or sets the widget type.</summary>
    public string WidgetType { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name in the layout's language.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the category.</summary>
    public WidgetCategory Category { get; set; }

    /// <summary>Gets or sets the view mode.</summary>
    public string ViewMode { get; set; } = WidgetViewMode.Landscape;

    /// <summary>Gets or sets a value indicating whether the section is featured.</summary>
    public bool Featured { get; set; }

    /// <summary>Gets or sets the "see all" route.</summary>
    public string? Route { get; set; }

    /// <summary>Gets or sets the instance data.</summary>
    public string? AdditionalData { get; set; }

    /// <summary>Gets or sets the order.</summary>
    public int Order { get; set; }

    /// <summary>Gets or sets the minimum item count.</summary>
    public int MinItems { get; set; }

    /// <summary>Gets or sets the maximum item count.</summary>
    public int MaxItems { get; set; }

    /// <summary>Gets or sets the limit factor.</summary>
    public int LimitFactor { get; set; } = 1;

    /// <summary>Gets or sets the most episodes per series.</summary>
    public int? MaxPerSeries { get; set; }

    /// <summary>Gets or sets the theme.</summary>
    public string? Theme { get; set; }

    /// <summary>Returns the stored fields of a descriptor.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <returns>The stored section.</returns>
    public static StoredSection From(WidgetDescriptor descriptor) => new()
    {
        WidgetType = descriptor.WidgetType,
        DisplayName = descriptor.DisplayName,
        Category = descriptor.Category,
        ViewMode = descriptor.ViewMode,
        Featured = descriptor.Featured,
        Route = descriptor.Route,
        AdditionalData = descriptor.AdditionalData,
        Order = descriptor.Order,
        MinItems = descriptor.MinItems,
        MaxItems = descriptor.MaxItems,
        LimitFactor = descriptor.LimitFactor,
        MaxPerSeries = descriptor.MaxPerSeries,
        Theme = descriptor.Theme
    };

    /// <summary>Returns the descriptor of the stored section.</summary>
    /// <param name="sectionKey">The section key.</param>
    /// <returns>A new descriptor.</returns>
    public WidgetDescriptor ToDescriptor(string sectionKey) => new()
    {
        WidgetType = WidgetType,
        DisplayName = DisplayName,
        Category = Category,
        ViewMode = ViewMode,
        Featured = Featured,
        Route = Route,
        AdditionalData = AdditionalData,
        SectionKey = sectionKey,
        Order = Order,
        MinItems = MinItems,
        MaxItems = MaxItems,
        LimitFactor = LimitFactor,
        MaxPerSeries = MaxPerSeries,
        Theme = Theme
    };
}
