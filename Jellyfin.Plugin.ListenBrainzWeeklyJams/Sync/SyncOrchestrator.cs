using System;
using System.Diagnostics;
using System.Net;
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
/// build-then-commit — an existing playlist is only touched after a fully
/// successful fetch and match (NFR-05). With D-17, a second target maintains
/// a playlist for the previous synced week.
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
        var latest = WeeklyJamsSelector.SelectLatest(playlists, options.ListenBrainzUsername);
        var latestWeek = WeeklyJamsSelector.GetWeekIdentifier(latest.CreatedAt);

        var state = _stateStore.Load();

        // D-17: last week is the second-newest created-for-you entry — the
        // same interpretation the ListenBrainz website uses. The list
        // retains the two most recent weeks (seconds apart under catch-up
        // load); a single entry means there is no last week yet.
        Guid? previousTarget = options.SyncLastWeek
            ? WeeklyJamsSelector
                .SelectPreviousWeekFromList(playlists, options.ListenBrainzUsername)
                ?.Mbid
            : null;

        // FR-25: the immutable playlist MBID is the change-detection identity,
        // per target.
        var latestUnchanged = !force && state.LastSyncedPlaylistMbid == latest.Mbid;
        var previousUnchanged = !options.SyncLastWeek
            || previousTarget is null
            || (!force && state.PreviousSyncedPlaylistMbid == previousTarget);

        if (latestUnchanged && previousUnchanged)
        {
            stopwatch.Stop();
            _logger.LogInformation("Weekly Jams already up to date (week of {Week})", latestWeek);
            return ReportFrom(state, latest, latestWeek, SyncOutcome.UpToDate, stopwatch.Elapsed);
        }

        var stateChanged = false;
        WeekSyncResult? latestResult = null;
        if (!latestUnchanged)
        {
            latestResult = await SyncOneWeekAsync(
                latest.Mbid,
                state.JellyfinPlaylistId,
                options.PlaylistName,
                options,
                cancellationToken).ConfigureAwait(false);
            if (latestResult.Synced)
            {
                state.LastSyncedPlaylistMbid = latest.Mbid;
                state.LastSyncedWeek = latestWeek;
                state.JellyfinPlaylistId = latestResult.PlaylistId;
                state.LastSyncAtUtc = DateTimeOffset.UtcNow;
                state.LastSummary = new SyncStateSummary(
                    SyncOutcome.Updated,
                    latestResult.Total,
                    latestResult.Matched,
                    latestResult.ViaMbid,
                    latestResult.ViaName,
                    latestResult.Missing,
                    latestResult.Ambiguous);
                stateChanged = true;
            }
        }

        if (options.SyncLastWeek && !previousUnchanged && previousTarget is not null)
        {
            try
            {
                var previousResult = await SyncOneWeekAsync(
                    previousTarget.Value,
                    state.PreviousJellyfinPlaylistId,
                    options.LastWeekPlaylistName,
                    options,
                    cancellationToken).ConfigureAwait(false);
                if (previousResult.Synced)
                {
                    state.PreviousSyncedPlaylistMbid = previousTarget;
                    state.PreviousJellyfinPlaylistId = previousResult.PlaylistId;
                    state.LastPreviousWeekSummary = previousResult.ToSummary();
                    stateChanged = true;
                }
            }
            catch (ListenBrainzHttpException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // The previous week's playlist has expired on ListenBrainz
                // (retention is limited); keep the current playlist contents
                // and retry on the next run.
                _logger.LogWarning(
                    e,
                    "The previous-week playlist {Mbid} is no longer available on ListenBrainz; keeping the current playlist contents",
                    previousTarget);
            }
        }

        if (stateChanged)
        {
            _stateStore.Save(state);
        }

        stopwatch.Stop();
        if (latestResult is not null)
        {
            return new SyncReport(
                latestResult.Outcome,
                latest.Mbid,
                latest.Title,
                latestResult.Week,
                latestResult.Total,
                latestResult.Matched,
                latestResult.ViaMbid,
                latestResult.ViaName,
                latestResult.Missing,
                latestResult.Ambiguous,
                stopwatch.Elapsed,
                state.LastPreviousWeekSummary);
        }

        // Latest unchanged (only the previous week was synced, or nothing
        // changed for it): report from the stored summary.
        return ReportFrom(state, latest, latestWeek, SyncOutcome.Updated, stopwatch.Elapsed);
    }

    private SyncReport ReportFrom(
        SyncState state,
        LbPlaylistSummary latest,
        string latestWeek,
        SyncOutcome outcome,
        TimeSpan duration)
    {
        var summary = state.LastSummary;
        return new SyncReport(
            outcome,
            latest.Mbid,
            latest.Title,
            latestWeek,
            summary?.Total ?? 0,
            summary?.Matched ?? 0,
            summary?.ViaMbid ?? 0,
            summary?.ViaName ?? 0,
            summary?.Missing ?? 0,
            summary?.Ambiguous ?? 0,
            duration,
            state.LastPreviousWeekSummary);
    }

    private async Task<WeekSyncResult> SyncOneWeekAsync(
        Guid playlistMbid,
        Guid? existingPlaylistId,
        string playlistName,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        var detail = await _client.GetPlaylistAsync(playlistMbid, cancellationToken).ConfigureAwait(false);
        var week = WeeklyJamsSelector.GetWeekIdentifier(detail.CreatedAt);
        var matchReport = _matcher.Resolve(detail.Tracks, null, cancellationToken);

        // FR-18: zero-match guard — never create or wipe the playlist on a
        // zero match, and do not advance state so the next run retries.
        if (matchReport.MatchedCount == 0)
        {
            _logger.LogWarning(
                "Zero of {Total} tracks matched the library for {PlaylistName} (week of {Week}); playlist left untouched",
                matchReport.Total,
                playlistName,
                week);
            return new WeekSyncResult(
                Synced: false,
                SyncOutcome.ZeroMatchGuard,
                week,
                matchReport.Total,
                0,
                0,
                0,
                matchReport.MissingCount,
                0,
                PlaylistId: existingPlaylistId ?? Guid.Empty,
                playlistMbid);
        }

        var playlistId = await _playlistWriter
            .WriteAsync(existingPlaylistId, matchReport.OrderedItemIds, playlistName, options, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "{PlaylistName} (week of {Week}): {Matched}/{Total} matched ({Mbid} MBID, {Name} name), {Missing} missing, {Ambiguous} ambiguous",
            playlistName,
            week,
            matchReport.MatchedCount,
            matchReport.Total,
            matchReport.ViaMbidCount,
            matchReport.ViaNameCount,
            matchReport.MissingCount,
            matchReport.AmbiguousCount);

        return new WeekSyncResult(
            Synced: true,
            SyncOutcome.Updated,
            week,
            matchReport.Total,
            matchReport.MatchedCount,
            matchReport.ViaMbidCount,
            matchReport.ViaNameCount,
            matchReport.MissingCount,
            matchReport.AmbiguousCount,
            playlistId,
            playlistMbid);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _syncGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record WeekSyncResult(
        bool Synced,
        SyncOutcome Outcome,
        string Week,
        int Total,
        int Matched,
        int ViaMbid,
        int ViaName,
        int Missing,
        int Ambiguous,
        Guid PlaylistId,
        Guid PlaylistMbid)
    {
        public WeekSummary ToSummary()
            => new(Outcome, Week, Total, Matched, ViaMbid, ViaName, Missing, Ambiguous);
    }
}
