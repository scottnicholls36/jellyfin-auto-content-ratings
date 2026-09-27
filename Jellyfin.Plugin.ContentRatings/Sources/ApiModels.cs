using System.Collections.Generic;
using System.Text.Json.Serialization;

// Minimal response shapes: only the fields the plugin reads.
#pragma warning disable CA2227, CA1002
namespace Jellyfin.Plugin.ContentRatings.Sources;

internal sealed class TmdbReleaseDatesResponse
{
    [JsonPropertyName("results")]
    public List<TmdbCountryReleases> Results { get; set; } = [];
}

internal sealed class TmdbCountryReleases
{
    [JsonPropertyName("iso_3166_1")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("release_dates")]
    public List<TmdbReleaseDate> ReleaseDates { get; set; } = [];
}

internal sealed class TmdbReleaseDate
{
    [JsonPropertyName("certification")]
    public string? Certification { get; set; }

    /// <summary>
    /// Gets or sets the release type: 1 premiere, 2 limited theatrical, 3 theatrical, 4 digital, 5 physical, 6 TV.
    /// </summary>
    [JsonPropertyName("type")]
    public int Type { get; set; }
}

internal sealed class TmdbContentRatingsResponse
{
    [JsonPropertyName("results")]
    public List<TmdbContentRating> Results { get; set; } = [];
}

internal sealed class TmdbContentRating
{
    [JsonPropertyName("iso_3166_1")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("rating")]
    public string? Rating { get; set; }
}

internal sealed class TmdbFindResponse
{
    [JsonPropertyName("movie_results")]
    public List<TmdbIdResult> MovieResults { get; set; } = [];

    [JsonPropertyName("tv_results")]
    public List<TmdbIdResult> TvResults { get; set; } = [];
}

internal sealed class TmdbIdResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

internal sealed class TvdbResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

internal sealed class TvdbLoginData
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

internal sealed class TvdbExtendedRecord
{
    [JsonPropertyName("contentRatings")]
    public List<TvdbContentRating>? ContentRatings { get; set; }
}

internal sealed class TvdbContentRating
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the ISO 3166-1 alpha-3 country code, lower case (e.g. <c>gbr</c>).
    /// </summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }
}

internal sealed class TvdbRemoteIdResult
{
    [JsonPropertyName("series")]
    public TvdbIdRecord? Series { get; set; }

    [JsonPropertyName("movie")]
    public TvdbIdRecord? Movie { get; set; }
}

internal sealed class TvdbIdRecord
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}
