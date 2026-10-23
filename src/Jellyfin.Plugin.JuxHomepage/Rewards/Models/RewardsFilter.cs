using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Rewards.Models;

/// <summary>
/// Filter parameters for a Rewards widget instance, as configured by the administrator. Stored
/// round-trip via a widget config row's <c>ExtraParams</c> (see <see cref="FromExtraParams"/>), the
/// same mechanism the Discover Movies widget uses (see
/// <see cref="TMDb.Models.TMDbDiscoverFilter"/>).
/// <para>
/// At least one of <see cref="CeremonyQid"/> or <see cref="CategoryQid"/> must be set (enforced on admin
/// Save by <see cref="Plugin.EnsureSavable"/>, logged on load) -- a bare <see cref="Year"/> alone is not a meaningful
/// Wikidata query (see TODO_V2.md Phase 14 research: award statements are always attached to an
/// award-category item, never to a year in isolation).
/// </para>
/// </summary>
public sealed partial class RewardsFilter
{
    /// <summary>
    /// Gets or sets the Wikidata Q-id of the ceremony (e.g. "Q19020" for the Academy Awards), used to
    /// select every film-attached award category that is an "instance of" this ceremony. Combine with
    /// <see cref="Year"/> to scope to a single edition.
    /// </summary>
    public string? CeremonyQid { get; set; }

    /// <summary>
    /// Gets or sets the Wikidata Q-id of a specific award category (e.g. "Q102427" for the Academy
    /// Award for Best Picture), used to select every film that won this exact category, across all
    /// editions unless <see cref="Year"/> is also set.
    /// </summary>
    public string? CategoryQid { get; set; }

    /// <summary>
    /// Gets or sets the ceremony/award year to filter by, if any. Combined with
    /// <see cref="CeremonyQid"/> and/or <see cref="CategoryQid"/> (at least one of which is required).
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Reconstructs a <see cref="RewardsFilter"/> from a Rewards widget instance's configured extra
    /// parameters (as forwarded by <see cref="Widgets.WidgetService"/>).
    /// <para>
    /// <see cref="CeremonyQid"/>/<see cref="CategoryQid"/> are validated against <see cref="QidPattern"/>
    /// before being accepted -- both are interpolated directly into a SPARQL query string by
    /// <see cref="WikidataApiClient"/>, so an admin-supplied value that does not look like a real
    /// Wikidata Q-id (e.g. one containing a SPARQL <c>SERVICE</c> clause) is silently ignored rather
    /// than passed through, exactly like an invalid instance GUID is already ignored elsewhere in
    /// Rewards (see <see cref="RewardsCacheService.GetRewards"/>).
    /// </para>
    /// </summary>
    /// <param name="extraParams">The instance's configured extra parameters.</param>
    /// <param name="logger">
    /// Optional logger used to record a warning when a Q-id is rejected. Omitted by callers that do not
    /// need this visibility (e.g. tests constructing a filter directly).
    /// </param>
    /// <param name="instanceId">
    /// Optional Rewards widget instance identifier, included in the warning log message for
    /// traceability when multiple Rewards instances are configured.
    /// </param>
    /// <returns>The reconstructed filter.</returns>
    public static RewardsFilter FromExtraParams(
        IReadOnlyDictionary<string, string> extraParams,
        ILogger? logger = null,
        string? instanceId = null)
    {
        var filter = new RewardsFilter();

        if (extraParams.TryGetValue(RewardsWidget.CeremonyQidParam, out var ceremonyQid) && !string.IsNullOrWhiteSpace(ceremonyQid))
        {
            if (QidPattern().IsMatch(ceremonyQid))
            {
                filter.CeremonyQid = ceremonyQid;
            }
            else
            {
                logger?.LogWarning(
                    "Rewards instance '{InstanceId}': ceremonyQid '{CeremonyQid}' is not a valid Wikidata Q-id; ignoring this filter.",
                    instanceId,
                    ceremonyQid);
            }
        }

        if (extraParams.TryGetValue(RewardsWidget.CategoryQidParam, out var categoryQid) && !string.IsNullOrWhiteSpace(categoryQid))
        {
            if (QidPattern().IsMatch(categoryQid))
            {
                filter.CategoryQid = categoryQid;
            }
            else
            {
                logger?.LogWarning(
                    "Rewards instance '{InstanceId}': categoryQid '{CategoryQid}' is not a valid Wikidata Q-id; ignoring this filter.",
                    instanceId,
                    categoryQid);
            }
        }

        if (extraParams.TryGetValue(RewardsWidget.YearParam, out var yearText)
            && int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            filter.Year = year;
        }

        return filter;
    }

    /// <summary>Matches a well-formed Wikidata Q-id (e.g. "Q102427"), nothing more.</summary>
    [GeneratedRegex(@"^Q[1-9]\d*$")]
    private static partial Regex QidPattern();
}
