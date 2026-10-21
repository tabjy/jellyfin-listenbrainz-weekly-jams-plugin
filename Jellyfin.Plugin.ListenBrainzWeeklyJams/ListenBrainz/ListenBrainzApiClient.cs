using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// ListenBrainz API client: created-for-you playlist list and playlist detail,
/// with rate limiting (FR-10), retries (NFR-05), required User-Agent (FR-09)
/// and actionable error mapping (FR-11).
/// </summary>
public sealed class ListenBrainzApiClient : IDisposable
{
    private const string ApiBaseUrl = "https://api.listenbrainz.org";
    private const int PlaylistPageSize = 25;
    private const int MaxHttpRetries = 3;
    private const int MaxRateLimitRetries = 5;
    private const double RateLimitBackoffBufferSeconds = 0.5;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly string UserAgent =
        $"Jellyfin.Plugin.ListenBrainzWeeklyJams/{typeof(ListenBrainzApiClient).Assembly.GetName().Version}"
        + " (https://github.com/tabjy/jellyfin-listenbrainz-recommendations-plugin)";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ListenBrainzApiClient> _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private long _lastRequestTimestamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListenBrainzApiClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public ListenBrainzApiClient(IHttpClientFactory httpClientFactory, ILogger<ListenBrainzApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets or sets the minimum spacing between ListenBrainz requests (FR-10:
    /// the API allows at most one call per second). Exposed for tests.
    /// </summary>
    public TimeSpan MinRequestInterval { get; set; } = TimeSpan.FromSeconds(1.0);

    /// <summary>
    /// Gets or sets the base delay for exponential backoff on retries.
    /// Exposed for tests.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1.0);

    /// <summary>
    /// Fetches all created-for-you playlists for a user, following pagination (FR-06).
    /// </summary>
    /// <param name="userName">The ListenBrainz username.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The playlist summaries.</returns>
    public async Task<IReadOnlyList<LbPlaylistSummary>> GetCreatedForPlaylistsAsync(
        string userName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        var summaries = new List<LbPlaylistSummary>();
        var offset = 0;
        while (true)
        {
            var url = $"{ApiBaseUrl}/1/user/{Uri.EscapeDataString(userName)}/playlists/createdfor"
                      + $"?count={PlaylistPageSize}&offset={offset}";

            CreatedForResponse page;
            try
            {
                page = await GetJsonAsync<CreatedForResponse>(url, cancellationToken).ConfigureAwait(false);
            }
            catch (ListenBrainzHttpException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ListenBrainzUserNotFoundException(userName);
            }

            var entries = page.Playlists ?? [];
            if (entries.Count == 0)
            {
                break;
            }

            foreach (var entry in entries)
            {
                if (entry.Playlist is not null)
                {
                    var summary = ListenBrainzMapper.ToSummary(entry.Playlist);
                    if (summary is not null)
                    {
                        summaries.Add(summary);
                    }
                }
            }

            offset += entries.Count;
            if (offset >= page.PlaylistCount)
            {
                break;
            }
        }

        _logger.LogInformation("Fetched {Count} created-for-you playlists for user {User}", summaries.Count, userName);
        return summaries;
    }

    /// <summary>
    /// Fetches a playlist's detail with ordered tracks (FR-08).
    /// </summary>
    /// <param name="playlistMbid">The ListenBrainz playlist MBID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The playlist detail.</returns>
    public async Task<LbPlaylistDetail> GetPlaylistAsync(Guid playlistMbid, CancellationToken cancellationToken)
    {
        var url = $"{ApiBaseUrl}/1/playlist/{playlistMbid}";

        PlaylistDetailResponse response;
        try
        {
            response = await GetJsonAsync<PlaylistDetailResponse>(url, cancellationToken).ConfigureAwait(false);
        }
        catch (ListenBrainzHttpException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ListenBrainzException(
                $"ListenBrainz playlist {playlistMbid} was not found (it may have expired).");
        }

        if (response.Playlist is null)
        {
            throw new ListenBrainzException($"ListenBrainz returned an empty response for playlist {playlistMbid}.");
        }

        return ListenBrainzMapper.ToDetail(response.Playlist);
    }

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            await EnforceRateLimitAsync(cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException
                                      && !cancellationToken.IsCancellationRequested)
            {
                if (attempt > MaxHttpRetries)
                {
                    throw new ListenBrainzException(
                        $"ListenBrainz is unreachable after {attempt} attempts: {e.Message}");
                }

                _logger.LogWarning(
                    e,
                    "ListenBrainz request failed (attempt {Attempt}/{Max}), retrying",
                    attempt,
                    MaxHttpRetries);
                await DelayRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content
                        .ReadFromJsonAsync<T>(JsonOptions, timeoutCts.Token)
                        .ConfigureAwait(false);

                    if (result is null)
                    {
                        throw new ListenBrainzException($"ListenBrainz returned an empty response for {url}.");
                    }

                    return result;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt <= MaxRateLimitRetries)
                {
                    var resetSeconds = ParseRateLimitReset(response);
                    _logger.LogWarning(
                        "ListenBrainz rate limit hit, backing off {Seconds:F1}s",
                        resetSeconds + RateLimitBackoffBufferSeconds);
                    await Task.Delay(
                        TimeSpan.FromSeconds(resetSeconds + RateLimitBackoffBufferSeconds),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if ((int)response.StatusCode >= 500 && attempt <= MaxHttpRetries)
                {
                    await DelayRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new ListenBrainzHttpException(
                    $"ListenBrainz returned {(int)response.StatusCode} ({response.StatusCode}) for {url}.",
                    response.StatusCode);
            }
        }
    }

    private Task DelayRetryAsync(int attempt, CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)) * RetryBaseDelay.TotalSeconds, cancellationToken);

    private static double ParseRateLimitReset(HttpResponseMessage response)
    {
        var header = response.Headers.TryGetValues("X-RateLimit-Reset-In", out var values)
            ? values.FirstOrDefault()
            : null;
        return double.TryParse(header, out var seconds) ? seconds : 1.0;
    }

    private async Task EnforceRateLimitAsync(CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var last = Interlocked.Read(ref _lastRequestTimestamp);
            if (last != 0)
            {
                var elapsedSeconds = (Stopwatch.GetTimestamp() - last) / (double)Stopwatch.Frequency;
                var minimumSeconds = MinRequestInterval.TotalSeconds;
                if (elapsedSeconds < minimumSeconds)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(minimumSeconds - elapsedSeconds),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            Interlocked.Exchange(ref _lastRequestTimestamp, Stopwatch.GetTimestamp());
        }
        finally
        {
            _requestGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _requestGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
