using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.ContentRatings.Sources;
using Xunit;

namespace Jellyfin.Plugin.ContentRatings.Tests;

public class RatingSelectorTests
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    private static T Load<T>(string name)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine("TestData", name)), _options)!;

    [Fact]
    public void TmdbMovie_PicksTheatricalCertificationForFirstCountry()
    {
        var response = Load<TmdbReleaseDatesResponse>("tmdb_movie_release_dates.json");

        var result = RatingSelector.SelectTmdbMovie(response, ["GB", "US"]);

        Assert.Equal(new SourceRating("18", "GB"), result);
    }

    [Fact]
    public void TmdbMovie_FallsBackToSecondCountry()
    {
        var response = Load<TmdbReleaseDatesResponse>("tmdb_movie_release_dates.json");

        var result = RatingSelector.SelectTmdbMovie(response, ["IE", "US"]);

        Assert.Equal(new SourceRating("R", "US"), result);
    }

    [Fact]
    public void TmdbMovie_IgnoresBlankCertifications()
    {
        var response = Load<TmdbReleaseDatesResponse>("tmdb_movie_release_dates.json");

        // DE's theatrical release has no certification, so the physical release is used.
        var result = RatingSelector.SelectTmdbMovie(response, ["DE"]);

        Assert.Equal(new SourceRating("18", "DE"), result);
    }

    [Fact]
    public void TmdbMovie_ReturnsNullWhenNoCountryMatches()
    {
        var response = Load<TmdbReleaseDatesResponse>("tmdb_movie_release_dates.json");

        Assert.Null(RatingSelector.SelectTmdbMovie(response, ["JP"]));
        Assert.Null(RatingSelector.SelectTmdbMovie(null, ["GB"]));
    }

    [Fact]
    public void TmdbTv_PicksRequestedCountryAndSkipsBlank()
    {
        var response = Load<TmdbContentRatingsResponse>("tmdb_tv_content_ratings.json");

        Assert.Equal(new SourceRating("15", "GB"), RatingSelector.SelectTmdbTv(response, ["GB"]));
        Assert.Equal(new SourceRating("TV-MA", "US"), RatingSelector.SelectTmdbTv(response, ["FR", "US"]));
    }

    [Fact]
    public void Tvdb_MatchesAlpha3Country()
    {
        var response = Load<TvdbResponse<TvdbExtendedRecord>>("tvdb_series_extended.json");

        Assert.Equal(new SourceRating("15", "GB"), RatingSelector.SelectTvdb(response.Data, ["GB"]));
        Assert.Equal(new SourceRating("TV-MA", "US"), RatingSelector.SelectTvdb(response.Data, ["AU", "US"]));
        Assert.Null(RatingSelector.SelectTvdb(response.Data, ["DE"]));
    }

    [Theory]
    [InlineData("15", "GB", false, "15")]
    [InlineData("15", "GB", true, "GB-15")]
    [InlineData("PG-13", "US", true, "PG-13")]
    [InlineData("16", "DE", true, "FSK-16")]
    public void Format_MatchesJellyfinConvention(string rating, string country, bool prefix, string expected)
    {
        Assert.Equal(expected, RatingSelector.Format(new SourceRating(rating, country), prefix));
    }

    [Theory]
    [InlineData("GB", "gbr")]
    [InlineData("us", "usa")]
    [InlineData("XX", null)]
    public void ToAlpha3_ConvertsKnownCountries(string alpha2, string? expected)
    {
        Assert.Equal(expected, RatingSelector.ToAlpha3(alpha2));
    }
}
