using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentRatings.Configuration;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.ContentRatings.Sources;

/// <summary>
/// A rating found by a source, with the country whose rating system it belongs to.
/// </summary>
/// <param name="Rating">The raw certification, e.g. <c>15</c>.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2 country code, upper case.</param>
public sealed record SourceRating(string Rating, string CountryCode);

/// <summary>
/// The outcome of a lookup: the id the item was found under (if any) and the rating (if any).
/// </summary>
/// <param name="Id">The source's own id for the item, or <c>null</c> if the item could not be identified.</param>
/// <param name="Rating">The rating, or <c>null</c> if the source has none for the requested countries.</param>
public sealed record RatingLookup(string? Id, SourceRating? Rating)
{
    /// <summary>
    /// Gets a result for an item the source could not identify.
    /// </summary>
    public static RatingLookup NotIdentified { get; } = new(null, null);
}

/// <summary>
/// Looks up content ratings from an online metadata service.
/// </summary>
public interface IRatingSource
{
    /// <summary>
    /// Gets the configuration name of this source, see <see cref="RatingSourceNames"/>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns whether the source has the credentials it needs.
    /// </summary>
    /// <param name="config">Current configuration.</param>
    /// <returns><c>true</c> if the source can be used.</returns>
    bool IsConfigured(PluginConfiguration config);

    /// <summary>
    /// Gets the rating for a movie or series, trying each country in order.
    /// </summary>
    /// <param name="item">The movie or series.</param>
    /// <param name="countryCodes">ISO 3166-1 alpha-2 codes in order of preference.</param>
    /// <param name="config">Current configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The id the item was found under and its rating, either of which may be missing.</returns>
    Task<RatingLookup> GetRatingAsync(
        BaseItem item,
        IReadOnlyList<string> countryCodes,
        PluginConfiguration config,
        CancellationToken cancellationToken);
}
