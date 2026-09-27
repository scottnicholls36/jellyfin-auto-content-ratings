using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
/// Ratings from TheTVDB (api4.thetvdb.com v4).
/// </summary>
public sealed class TvdbRatingSource : IRatingSource
{
    private const string BaseUrl = "https://api4.thetvdb.com/v4/";

    // Tokens last a month; refresh well before that.
    private static readonly TimeSpan _tokenLifetime = TimeSpan.FromDays(7);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HttpHelper _http = new(TimeSpan.FromMilliseconds(100));
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _token;
    private string? _tokenCredentials;
    private DateTime _tokenExpiresUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvdbRatingSource"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    public TvdbRatingSource(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => RatingSourceNames.Tvdb;

    /// <inheritdoc />
    public bool IsConfigured(PluginConfiguration config) => !string.IsNullOrWhiteSpace(config.TvdbApiKey);

    /// <inheritdoc />
    public async Task<SourceRating?> GetRatingAsync(
        BaseItem item,
        IReadOnlyList<string> countryCodes,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var kind = item switch
        {
            Movie => "movies",
            Series => "series",
            _ => null
        };

        if (kind is null)
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient(NamedClient.Default);

        var id = await ResolveIdAsync(client, config, item, isMovie: kind == "movies", cancellationToken).ConfigureAwait(false);
        if (id is null)
        {
            return null;
        }

        var response = await GetAsync<TvdbResponse<TvdbExtendedRecord>>(client, config, $"{kind}/{id}/extended", cancellationToken).ConfigureAwait(false);
        return RatingSelector.SelectTvdb(response?.Data, countryCodes);
    }

    private async Task<string?> ResolveIdAsync(HttpClient client, PluginConfiguration config, BaseItem item, bool isMovie, CancellationToken cancellationToken)
    {
        if (item.TryGetProviderId(MetadataProvider.Tvdb, out var tvdbId) && !string.IsNullOrWhiteSpace(tvdbId))
        {
            return tvdbId;
        }

        // No TVDB id: search by IMDb or TMDB id and keep the result of the right type.
        foreach (var provider in new[] { MetadataProvider.Imdb, MetadataProvider.Tmdb })
        {
            if (!item.TryGetProviderId(provider, out var remoteId) || string.IsNullOrWhiteSpace(remoteId))
            {
                continue;
            }

            var found = await GetAsync<TvdbResponse<List<TvdbRemoteIdResult>>>(
                client,
                config,
                "search/remoteid/" + Uri.EscapeDataString(remoteId),
                cancellationToken).ConfigureAwait(false);

            var match = found?.Data?
                .Select(r => isMovie ? r.Movie : r.Series)
                .FirstOrDefault(r => r is not null);

            if (match is not null)
            {
                return match.Id.ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private async Task<T?> GetAsync<T>(HttpClient client, PluginConfiguration config, string path, CancellationToken cancellationToken)
    {
        // One retry with a fresh token in case the cached one was revoked or expired early.
        for (var attempt = 1; ; attempt++)
        {
            var token = await GetTokenAsync(client, config, forceRefresh: attempt > 1, cancellationToken).ConfigureAwait(false);
            try
            {
                return await _http.SendAsync<T>(
                    client,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        return request;
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HttpUnauthorizedException) when (attempt == 1)
            {
            }
            catch (HttpUnauthorizedException)
            {
                throw new RatingSourceAuthException("TVDB rejected the request after logging in again. Check the API key and PIN.");
            }
        }
    }

    private async Task<string> GetTokenAsync(HttpClient client, PluginConfiguration config, bool forceRefresh, CancellationToken cancellationToken)
    {
        var apiKey = config.TvdbApiKey.Trim();
        var pin = config.TvdbPin.Trim();
        var credentials = apiKey + "\n" + pin;

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _token is not null && _tokenCredentials == credentials && DateTime.UtcNow < _tokenExpiresUtc)
            {
                return _token;
            }

            // Serialised up front so the request has a Content-Length; JsonContent streams it chunked,
            // which not every server or proxy in front of TVDB accepts.
            var body = JsonSerializer.Serialize(
                string.IsNullOrEmpty(pin) ? new Dictionary<string, string> { ["apikey"] = apiKey } : new Dictionary<string, string> { ["apikey"] = apiKey, ["pin"] = pin });

            TvdbResponse<TvdbLoginData>? response;
            try
            {
                response = await _http.SendAsync<TvdbResponse<TvdbLoginData>>(
                    client,
                    () => new HttpRequestMessage(HttpMethod.Post, BaseUrl + "login") { Content = new StringContent(body, Encoding.UTF8, "application/json") },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HttpUnauthorizedException)
            {
                throw new RatingSourceAuthException("TVDB rejected the API key or PIN. Check them on the plugin settings page.");
            }

            var token = response?.Data?.Token;
            if (string.IsNullOrEmpty(token))
            {
                throw new RatingSourceAuthException("TVDB login returned no token.");
            }

            _token = token;
            _tokenCredentials = credentials;
            _tokenExpiresUtc = DateTime.UtcNow + _tokenLifetime;
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
