using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// The sync pipeline (design §3.4): fetch, select, short-circuit, match,
/// zero-match guard, write, persist state. Single-flight (D-13) and
/// build-then-commit — the existing playlist is only touched after a fully
/// successful fetch and match (NFR-05).
/// </summary>
public sealed class SyncOrchestrator : IDisposable
{
    private readonly IListenBrainzApiClient _client;
    private readonly LibraryMatcher _matcher;
    private readonly IPlaylistWriter _playlistWriter;
    private readonly SyncStateStore _stateStore;
    private readonly ILogger<SyncOrchestrator> _logger;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncOrchestrator"/> class.
    /// </summary>
    /// <param name="client">The ListenBrainz API client.</param>
    /// <param name="matcher">The library matcher.</param>
    /// <param name="playlistWriter">The playlist writer.</param>
    /// <param name="stateStore">The sync state store.</param>
    /// <param name="logger">The logger.</param>
    public SyncOrchestrator(
        IListenBrainzApiClient client,
        LibraryMatcher matcher,
        IPlaylistWriter playlistWriter,
        SyncStateStore stateStore,
        ILogger<SyncOrchestrator> logger)
    {
        _client = client;
        _matcher = matcher;
        _playlistWriter = playlistWriter;
        _stateStore = stateStore;
        _logger = logger;
    }

    /// <summary>
    /// Runs a sync.
    /// </summary>
    /// <param name="options">The sync options.</param>
    /// <param name="force">Bypasses the up-to-date short-circuit (FR-25:
    /// the config-page Sync now button).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sync report.</returns>
    public async Task<SyncReport> SyncAsync(SyncOptions options, bool force, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ListenBrainzUsername))
        {
            throw new ListenBrainzException(
                "The ListenBrainz username is not configured. Set it in the plugin configuration.");
        }

        if (!await _syncGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Weekly Jams sync skipped: already running");
            throw new SyncInProgressException();
        }

        try
        {
            return await SyncCoreAsync(options, force, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<SyncReport> SyncCoreAsync(SyncOptions options, bool force, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var playlists = await _client.GetCreatedForPlaylistsAsync(options.ListenBrainzUsername, cancellationToken)
            .ConfigureAwait(false);
        var selected = WeeklyJamsSelector.SelectLatest(playlists, options.ListenBrainzUsername);
        var week = selected.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var state = _stateStore.Load();

        // FR-25: the immutable playlist MBID is the change-detection identity.
        if (!force && state.LastSyncedPlaylistMbid == selected.Mbid)
        {
            stopwatch.Stop();
            _logger.LogInformation("Weekly Jams already up to date (week of {Week})", week);
            var last = state.LastSummary;
            return new SyncReport(
                SyncOutcome.UpToDate,
                selected.Mbid,
                selected.Title,
                week,
                last?.Total ?? 0,
                last?.Matched ?? 0,
                last?.ViaMbid ?? 0,
                last?.ViaName ?? 0,
                last?.Missing ?? 0,
                last?.Ambiguous ?? 0,
                stopwatch.Elapsed);
        }

        var detail = await _client.GetPlaylistAsync(selected.Mbid, cancellationToken).ConfigureAwait(false);
        var matchReport = _matcher.Resolve(detail.Tracks, null, cancellationToken);

        // FR-18: zero-match guard — never create or wipe the playlist on a
        // zero match, and do not advance state so the next run retries.
        if (matchReport.MatchedCount == 0)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Zero of {Total} tracks matched the library; playlist left untouched and state not advanced",
                matchReport.Total);
            return new SyncReport(
                SyncOutcome.ZeroMatchGuard,
                selected.Mbid,
                selected.Title,
                week,
                matchReport.Total,
                0,
                0,
                0,
                matchReport.MissingCount,
                0,
                stopwatch.Elapsed);
        }

        var playlistId = await _playlistWriter
            .WriteAsync(state.JellyfinPlaylistId, matchReport.OrderedItemIds, options, cancellationToken)
            .ConfigureAwait(false);

        _stateStore.Save(new SyncState
        {
            LastSyncedPlaylistMbid = selected.Mbid,
            LastSyncedWeek = week,
            LastSyncAtUtc = DateTimeOffset.UtcNow,
            JellyfinPlaylistId = playlistId,
            LastSummary = new SyncStateSummary(
                SyncOutcome.Updated,
                matchReport.Total,
                matchReport.MatchedCount,
                matchReport.ViaMbidCount,
                matchReport.ViaNameCount,
                matchReport.MissingCount,
                matchReport.AmbiguousCount)
        });

        stopwatch.Stop();
        _logger.LogInformation(
            "Weekly Jams sync complete (week of {Week}): {Matched}/{Total} matched ({Mbid} MBID, {Name} name), {Missing} missing, {Ambiguous} ambiguous in {Duration:F1}s",
            week,
            matchReport.MatchedCount,
            matchReport.Total,
            matchReport.ViaMbidCount,
            matchReport.ViaNameCount,
            matchReport.MissingCount,
            matchReport.AmbiguousCount,
            stopwatch.Elapsed.TotalSeconds);

        return new SyncReport(
            SyncOutcome.Updated,
            selected.Mbid,
            selected.Title,
            week,
            matchReport.Total,
            matchReport.MatchedCount,
            matchReport.ViaMbidCount,
            matchReport.ViaNameCount,
            matchReport.MissingCount,
            matchReport.AmbiguousCount,
            stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _syncGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
