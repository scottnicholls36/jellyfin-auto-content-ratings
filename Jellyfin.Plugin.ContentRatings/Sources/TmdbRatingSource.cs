using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentRatings.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.ContentRatings.Sources;

/// <summary>
/// Ratings from The Movie Database (api.themoviedb.org v3).
/// </summary>
public sealed class TmdbRatingSource : IRatingSource
{
    private const string BaseUrl = "https://api.themoviedb.org/3/";

    private readonly IHttpClientFactory _httpClientFactory;

    // TMDB allows roughly 40-50 requests a second; stay well under it.
    private readonly HttpHelper _http = new(TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbRatingSource"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    public TmdbRatingSource(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => RatingSourceNames.Tmdb;

    /// <inheritdoc />
    public bool IsConfigured(PluginConfiguration config) => !string.IsNullOrWhiteSpace(config.TmdbApiKey);

    /// <inheritdoc />
    public async Task<RatingLookup> GetRatingAsync(
        BaseItem item,
        IReadOnlyList<string> countryCodes,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var apiKey = config.TmdbApiKey.Trim();
        var client = _httpClientFactory.CreateClient(NamedClient.Default);

        try
        {
            switch (item)
            {
                case Movie:
                {
                    var id = await ResolveIdAsync(client, apiKey, item, isTv: false, cancellationToken).ConfigureAwait(false);
                    if (id is null)
                    {
                        return RatingLookup.NotIdentified;
                    }

                    var response = await GetAsync<TmdbReleaseDatesResponse>(client, apiKey, $"movie/{id}/release_dates", cancellationToken).ConfigureAwait(false);
                    return new RatingLookup(id, RatingSelector.SelectTmdbMovie(response, countryCodes));
                }

                case Series:
                {
                    var id = await ResolveIdAsync(client, apiKey, item, isTv: true, cancellationToken).ConfigureAwait(false);
                    if (id is null)
                    {
                        return RatingLookup.NotIdentified;
                    }

                    var response = await GetAsync<TmdbContentRatingsResponse>(client, apiKey, $"tv/{id}/content_ratings", cancellationToken).ConfigureAwait(false);
                    return new RatingLookup(id, RatingSelector.SelectTmdbTv(response, countryCodes));
                }

                default:
                    return RatingLookup.NotIdentified;
            }
        }
        catch (HttpUnauthorizedException)
        {
            throw new RatingSourceAuthException("TMDB rejected the API key. Check it on the plugin settings page.");
        }
    }

    private async Task<string?> ResolveIdAsync(HttpClient client, string apiKey, BaseItem item, bool isTv, CancellationToken cancellationToken)
    {
        if (item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbId) && !string.IsNullOrWhiteSpace(tmdbId))
        {
            return tmdbId;
        }

        // No TMDB id: look it up from another id the item does have.
        var lookups = new List<(MetadataProvider Provider, string Source)> { (MetadataProvider.Imdb, "imdb_id") };
        if (isTv)
        {
            lookups.Add((MetadataProvider.Tvdb, "tvdb_id"));
        }

        foreach (var (provider, source) in lookups)
        {
            if (!item.TryGetProviderId(provider, out var externalId) || string.IsNullOrWhiteSpace(externalId))
            {
                continue;
            }

            var found = await GetAsync<TmdbFindResponse>(
                client,
                apiKey,
                $"find/{Uri.EscapeDataString(externalId)}?external_source={source}",
                cancellationToken).ConfigureAwait(false);

            var match = (isTv ? found?.TvResults : found?.MovieResults)?.FirstOrDefault();
            if (match is not null)
            {
                return match.Id.ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private Task<T?> GetAsync<T>(HttpClient client, string apiKey, string path, CancellationToken cancellationToken)
    {
        // A v4 read access token is a JWT and goes in the Authorization header; a v3 key goes in the query string.
        var isBearer = apiKey.StartsWith("eyJ", StringComparison.Ordinal);
        var url = BaseUrl + path;
        if (!isBearer)
        {
            url += (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "api_key=" + Uri.EscapeDataString(apiKey);
        }

        return _http.SendAsync<T>(
            client,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (isBearer)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                return request;
            },
            cancellationToken);
    }
}
