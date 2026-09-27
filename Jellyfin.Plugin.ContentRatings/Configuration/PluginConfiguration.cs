using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ContentRatings.Configuration;

/// <summary>
/// Plugin configuration, persisted as XML by the server.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the data source for movies: <c>Tmdb</c> or <c>Tvdb</c>.
    /// Named <c>Source</c> because it predates <see cref="SeriesSource"/>; existing settings keep working.
    /// </summary>
    public string Source { get; set; } = RatingSourceNames.Tmdb;

    /// <summary>
    /// Gets or sets the data source for TV series: <c>Tmdb</c> or <c>Tvdb</c>.
    /// Empty means the same as <see cref="Source"/>.
    /// </summary>
    public string SeriesSource { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TMDB API key (v3 key or v4 read access token).
    /// </summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TVDB v4 API key.
    /// </summary>
    public string TvdbApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional TVDB subscriber PIN.
    /// </summary>
    public string TvdbPin { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to try the other source when the chosen one has no rating.
    /// Only used when that source also has an API key.
    /// </summary>
    public bool FallbackToOtherSource { get; set; }

    /// <summary>
    /// Gets or sets the ISO 3166-1 alpha-2 country whose rating system is used, e.g. <c>GB</c>.
    /// </summary>
    public string CountryCode { get; set; } = "GB";

    /// <summary>
    /// Gets or sets an optional second country to use when the first has no rating, e.g. <c>US</c>.
    /// </summary>
    public string FallbackCountryCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ids of the libraries (collection folders) the plugin updates.
    /// </summary>
    public string[] LibraryIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets how often, in hours, to check for new media. 0 means manual only.
    /// </summary>
    public int NewMediaIntervalHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets how often, in days, to re-check the whole library. 0 means manual only.
    /// </summary>
    public int FullScanIntervalDays { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to check for new media after every library scan.
    /// </summary>
    public bool RunAfterLibraryScan { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an existing rating may be replaced.
    /// </summary>
    public bool OverwriteExisting { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether new-media checks also retry items that still have no rating.
    /// </summary>
    public bool RetryUnrated { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a series' rating is copied to its seasons and episodes,
    /// mirroring what Jellyfin's own metadata editor does.
    /// </summary>
    public bool ApplyToSeasonsAndEpisodes { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to store ratings with a country prefix, e.g. <c>GB-15</c>.
    /// </summary>
    public bool IncludeCountryPrefix { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to lock the rating field after writing it,
    /// so a later metadata refresh cannot change it.
    /// </summary>
    public bool LockAfterUpdate { get; set; }

    /// <summary>
    /// Gets the source actually used for TV series.
    /// </summary>
    /// <returns><see cref="SeriesSource"/>, or <see cref="Source"/> when it is not set.</returns>
    public string GetSeriesSource() => string.IsNullOrWhiteSpace(SeriesSource) ? Source : SeriesSource;
}
