using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ContentRatings.Sources;

/// <summary>
/// Shared request plumbing: polite spacing between calls and retries on rate limiting.
/// </summary>
internal sealed class HttpHelper
{
    private const int MaxAttempts = 4;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval;
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public HttpHelper(TimeSpan minInterval)
    {
        _minInterval = minInterval;
    }

    /// <summary>
    /// Sends a request and deserialises the JSON body.
    /// </summary>
    /// <returns>The body, or <c>default</c> on 404.</returns>
    /// <exception cref="HttpUnauthorizedException">The server returned 401.</exception>
    public async Task<T?> SendAsync<T>(HttpClient client, Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await WaitForSlotAsync(cancellationToken).ConfigureAwait(false);

            using var request = requestFactory();
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new HttpUnauthorizedException();
            }

            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (retryable && attempt < MaxAttempts)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _lastRequestUtc + _minInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Raised by <see cref="HttpHelper"/> when a request is rejected with 401.
/// </summary>
internal sealed class HttpUnauthorizedException : Exception
{
}
