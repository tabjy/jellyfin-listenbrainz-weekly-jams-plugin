using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Playlists;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Tests;

public sealed class SyncOrchestratorTests : IDisposable
{
    private static readonly Guid ThisWeekMbid = Guid.Parse("e3d029d6-d8bc-4443-ad64-79fc442c1088");
    private static readonly Guid LastWeekMbid = Guid.Parse("c576fd05-7496-4899-8a15-cfa54e7daeab");
    private static readonly Guid NewWeekMbid = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid OldWeekMbid = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"lbwj-orch-{Guid.NewGuid():N}.json");
    private readonly FakeListenBrainzClient _client = new();
    private readonly FakeLibraryFacade _facade = new();
    private readonly FakePlaylistWriter _playlistWriter = new();

    private static SyncOptions Options(bool syncLastWeek = false)
        => new("tabjy", "Weekly Jams", OwningUserId: Guid.NewGuid(), IsPublic: true, SyncLastWeek: syncLastWeek);

    [Fact]
    public async Task FullSync_MatchesWritesAndAdvancesState()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        var item = NewItem(mbid);
        _facade.Items = [item];
        AddWeek(
            ThisWeekMbid,
            day: 13,
            tracks:
            [
                NewTrack(mbid),
                new LbTrack(2, "ffffffff-ffff-ffff-ffff-ffffffffffff", "Ghost Track", "Ghost Artist", "X", [])
            ]);

        var report = await CreateOrchestrator().SyncAsync(Options(), force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(2, report.Total);
        Assert.Equal(1, report.Matched);
        Assert.Equal(1, report.Missing);
        // Created 2026-10-13 (Tuesday) -> week identifier is Monday 2026-10-12.
        Assert.Equal("2026-10-12", report.Week);
        Assert.Equal([item.Id], _playlistWriter.LastItemIds);
        Assert.Null(report.PreviousWeek);
        var state = LoadState();
        Assert.Equal(ThisWeekMbid, state.LastSyncedPlaylistMbid);
        Assert.NotNull(state.JellyfinPlaylistId);
        Assert.Equal(SyncOutcome.Updated, state.LastSummary!.Outcome);
        Assert.Null(state.PreviousSyncedPlaylistMbid);
    }

    [Fact]
    public async Task SecondRun_ShortCircuitsWithoutDetailFetchOrWrite()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        AddWeek(ThisWeekMbid, day: 13, tracks: [NewTrack(mbid)]);
        var orchestrator = CreateOrchestrator();

        await orchestrator.SyncAsync(Options(), force: false, CancellationToken.None);
        _playlistWriter.Reset();
        _client.ResetCounters();

        var report = await orchestrator.SyncAsync(Options(), force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.UpToDate, report.Outcome);
        Assert.Equal(1, _client.ListCalls);
        Assert.Equal(0, _client.DetailCalls);
        Assert.Equal(0, _playlistWriter.WriteCalls);
    }

    [Fact]
    public async Task ForcedRun_BypassesShortCircuit()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        AddWeek(ThisWeekMbid, day: 13, tracks: [NewTrack(mbid)]);
        var orchestrator = CreateOrchestrator();

        await orchestrator.SyncAsync(Options(), force: false, CancellationToken.None);
        _playlistWriter.Reset();
        _client.ResetCounters();

        var report = await orchestrator.SyncAsync(Options(), force: true, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(1, _client.DetailCalls);
        Assert.Equal(1, _playlistWriter.WriteCalls);
    }

    [Fact]
    public async Task NewWeek_SyncsAgain()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        AddWeek(ThisWeekMbid, day: 13, tracks: [NewTrack(mbid)]);
        var orchestrator = CreateOrchestrator();
        await orchestrator.SyncAsync(Options(), force: false, CancellationToken.None);

        // ListenBrainz published the next week; the old week is now the
        // second-newest list entry.
        AddWeek(NewWeekMbid, day: 20, tracks: [NewTrack(mbid)]);
        _client.Summaries = [_client.Summaries[1], _client.Summaries[0]];
        _playlistWriter.Reset();

        var report = await orchestrator.SyncAsync(Options(), force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(1, _playlistWriter.WriteCalls);
        Assert.Equal(NewWeekMbid, LoadState().LastSyncedPlaylistMbid);
    }

    [Fact]
    public async Task ZeroMatches_GuardDoesNotTouchPlaylistOrAdvanceState()
    {
        _facade.Items = [NewItem("71d6ceac-9589-4235-a419-6d06e29429d1")];
        AddWeek(
            ThisWeekMbid,
            day: 13,
            tracks: [new LbTrack(1, "00000000-0000-0000-0000-0000000000ff", "Ghost Track", "Ghost Artist", "X", [])]);

        var report = await CreateOrchestrator().SyncAsync(Options(), force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.ZeroMatchGuard, report.Outcome);
        Assert.Equal(0, _playlistWriter.WriteCalls);
        Assert.Null(LoadState().LastSyncedPlaylistMbid);

        // State was not advanced, so a subsequent run retries the same week.
        _facade.Items = [NewItem("00000000-0000-0000-0000-0000000000ff")];
        _playlistWriter.Reset();
        var retry = await CreateOrchestrator().SyncAsync(Options(), force: false, CancellationToken.None);
        Assert.Equal(SyncOutcome.Updated, retry.Outcome);
    }

    [Fact]
    public async Task EmptyUsername_ThrowsActionableError()
    {
        var orchestrator = CreateOrchestrator();

        var exception = await Assert.ThrowsAsync<ListenBrainzException>(
            () => orchestrator.SyncAsync(
                new SyncOptions(" ", "Weekly Jams", null, true),
                force: false,
                CancellationToken.None));

        Assert.Contains("username", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentSync_SecondCallThrowsSingleFlight()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        AddWeek(ThisWeekMbid, day: 13, tracks: [NewTrack(mbid)]);
        _client.DelayDetail = TimeSpan.FromMilliseconds(200);
        var orchestrator = CreateOrchestrator();

        var firstTask = orchestrator.SyncAsync(Options(), force: false, CancellationToken.None);
        await Assert.ThrowsAsync<SyncInProgressException>(
            () => orchestrator.SyncAsync(Options(), force: false, CancellationToken.None));
        var first = await firstTask;

        Assert.Equal(SyncOutcome.Updated, first.Outcome);
    }

    // --- D-17: last-week syncing ---------------------------------------------

    [Fact]
    public async Task LastWeekDisabled_WritesOnlyMainPlaylist()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        AddCatchUpBatch(ThisWeekMbid, LastWeekMbid, mbid);

        var report = await CreateOrchestrator().SyncAsync(Options(syncLastWeek: false), force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(["Weekly Jams"], _playlistWriter.WrittenNames);
        Assert.Null(report.PreviousWeek);
        Assert.Null(LoadState().PreviousSyncedPlaylistMbid);
    }

    [Fact]
    public async Task LastWeekEnabled_SyncsBothPlaylists()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        var item = NewItem(mbid);
        _facade.Items = [item];
        AddCatchUpBatch(ThisWeekMbid, LastWeekMbid, mbid);

        var report = await CreateOrchestrator().SyncAsync(Options(syncLastWeek: true), force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(["Weekly Jams", "Last Week's Jams"], _playlistWriter.WrittenNames);
        Assert.Equal([item.Id], _playlistWriter.ItemsByName["Weekly Jams"]);
        Assert.Equal([item.Id], _playlistWriter.ItemsByName["Last Week's Jams"]);

        var state = LoadState();
        Assert.Equal(ThisWeekMbid, state.LastSyncedPlaylistMbid);
        Assert.Equal(LastWeekMbid, state.PreviousSyncedPlaylistMbid);
        Assert.NotNull(state.PreviousJellyfinPlaylistId);
        Assert.Equal("2026-10-12", report.PreviousWeek!.Week);
        Assert.Equal(1, report.PreviousWeek.Matched);
    }

    [Fact]
    public async Task LastWeekEnabled_SecondRun_ShortCircuitsBoth()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        AddCatchUpBatch(ThisWeekMbid, LastWeekMbid, mbid);
        var orchestrator = CreateOrchestrator();

        await orchestrator.SyncAsync(Options(syncLastWeek: true), force: false, CancellationToken.None);
        _playlistWriter.Reset();
        _client.ResetCounters();

        var report = await orchestrator.SyncAsync(Options(syncLastWeek: true), force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.UpToDate, report.Outcome);
        Assert.Equal(1, _client.ListCalls);
        Assert.Equal(0, _client.DetailCalls);
        Assert.Equal(0, _playlistWriter.WriteCalls);
        Assert.NotNull(report.PreviousWeek);
    }

    [Fact]
    public async Task LastWeekEnabled_NewWeek_PreviousBecomesOldLatest()
    {
        // The implicit rotation: when a new week appears, the outgoing latest
        // becomes the second-newest list entry, so the last-week playlist
        // takes over the week that was in the main playlist.
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        AddCatchUpBatch(ThisWeekMbid, OldWeekMbid, mbid);
        var orchestrator = CreateOrchestrator();
        await orchestrator.SyncAsync(Options(syncLastWeek: true), force: false, CancellationToken.None);

        AddWeek(NewWeekMbid, day: 20, tracks: [NewTrack(mbid)]);
        _client.Summaries = [_client.Summaries[2], _client.Summaries[0], _client.Summaries[1]];
        _playlistWriter.Reset();

        await orchestrator.SyncAsync(Options(syncLastWeek: true), force: false, CancellationToken.None);

        Assert.Equal(["Weekly Jams", "Last Week's Jams"], _playlistWriter.WrittenNames);
        var state = LoadState();
        Assert.Equal(NewWeekMbid, state.LastSyncedPlaylistMbid);
        Assert.Equal(ThisWeekMbid, state.PreviousSyncedPlaylistMbid);
    }

    [Fact]
    public async Task LastWeekEnabled_OnlyOneEntry_SkipsPreviousGracefully()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        AddWeek(ThisWeekMbid, day: 13, tracks: [NewTrack(mbid)]);

        var report = await CreateOrchestrator().SyncAsync(Options(syncLastWeek: true), force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(["Weekly Jams"], _playlistWriter.WrittenNames);
        Assert.Null(report.PreviousWeek);
        Assert.Null(LoadState().PreviousSyncedPlaylistMbid);
    }

    [Fact]
    public async Task LastWeekEnabled_PreviousExpiredOnListenBrainz_KeepsPlaylistAndSyncsMain()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        AddCatchUpBatch(ThisWeekMbid, LastWeekMbid, mbid);
        _client.Expire(LastWeekMbid);

        var report = await CreateOrchestrator().SyncAsync(Options(syncLastWeek: true), force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(["Weekly Jams"], _playlistWriter.WrittenNames);
        Assert.Equal(ThisWeekMbid, LoadState().LastSyncedPlaylistMbid);
        Assert.Null(LoadState().PreviousSyncedPlaylistMbid);
    }

    [Fact]
    public async Task LastWeekEnabled_ZeroMatchGuardOnPrevious_LeavesPreviousUntouched()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        AddCatchUpBatch(ThisWeekMbid, LastWeekMbid, mbid);
        // Last week's playlist matches nothing (its only track is a ghost).
        _client.Details[LastWeekMbid] = new LbPlaylistDetail(
            LastWeekMbid,
            "Weekly Jams for tabjy, last week",
            new DateTimeOffset(2026, 10, 13, 12, 31, 15, TimeSpan.Zero),
            [new LbTrack(1, "00000000-0000-0000-0000-0000000000ee", "Ghost Track", "Ghost Artist", "X", [])]);

        var report = await CreateOrchestrator().SyncAsync(Options(syncLastWeek: true), force: false, CancellationToken.None);

        // Main week synced; the previous week guarded and untouched.
        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(["Weekly Jams"], _playlistWriter.WrittenNames);
        var state = LoadState();
        Assert.Equal(ThisWeekMbid, state.LastSyncedPlaylistMbid);
        Assert.Null(state.PreviousSyncedPlaylistMbid);
    }

    private SyncOrchestrator CreateOrchestrator()
        => new(
            _client,
            new LibraryMatcher(_facade, NullLogger<LibraryMatcher>.Instance) { PageSize = 100 },
            _playlistWriter,
            new SyncStateStore(_statePath, NullLogger<SyncStateStore>.Instance),
            NullLogger<SyncOrchestrator>.Instance);

    private SyncState LoadState()
        => new SyncStateStore(_statePath, NullLogger<SyncStateStore>.Instance).Load();

    /// <summary>
    /// Adds a week (list entry + fetchable detail) created on the given day
    /// of October 2026.
    /// </summary>
    private void AddWeek(Guid mbid, int day, IReadOnlyList<LbTrack> tracks)
    {
        var created = new DateTimeOffset(2026, 10, day, 12, 32, 24, TimeSpan.Zero);
        var title = $"Weekly Jams for tabjy, week of 2026-10-{day - 1:00} Mon";
        _client.Summaries = [.. _client.Summaries, new LbPlaylistSummary(mbid, title, created, "weekly-jams")];
        _client.Details[mbid] = new LbPlaylistDetail(mbid, title, created, tracks);
    }

    /// <summary>
    /// Sets up a ListenBrainz catch-up batch: this week's and last week's
    /// playlists created 69 seconds apart (D-17).
    /// </summary>
    private void AddCatchUpBatch(Guid thisWeek, Guid lastWeek, string recordingMbid)
    {
        _client.Summaries = [];
        _client.Details.Clear();
        AddWeek(lastWeek, day: 13, tracks: [NewTrack(recordingMbid)]);
        AddWeek(thisWeek, day: 13, tracks: [NewTrack(recordingMbid)]);
        var summaries = _client.Summaries.ToList();
        summaries[0] = summaries[0] with { CreatedAt = summaries[0].CreatedAt.AddSeconds(-69) };
        _client.Summaries = summaries;
    }

    private static LbTrack NewTrack(string recordingMbid)
        => new(1, recordingMbid, "Some Track", "Some Artist", "Some Album", []);

    private static Audio NewItem(string recordingMbid)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = "Some Track",
            Artists = ["Some Artist"],
            ProviderIds = new Dictionary<string, string> { [LibraryMatcher.RecordingProviderId] = recordingMbid }
        };

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_statePath))
        {
            File.Delete(_statePath);
        }
    }

    private sealed class FakeListenBrainzClient : IListenBrainzApiClient
    {
        public IReadOnlyList<LbPlaylistSummary> Summaries { get; set; } = [];

        public Dictionary<Guid, LbPlaylistDetail> Details { get; } = [];

        public TimeSpan DelayDetail { get; set; } = TimeSpan.Zero;

        public int ListCalls { get; private set; }

        public int DetailCalls { get; private set; }

        public void ResetCounters()
        {
            ListCalls = 0;
            DetailCalls = 0;
        }

        public void Expire(Guid playlistMbid)
            => Details.Remove(playlistMbid);

        public Task<IReadOnlyList<LbPlaylistSummary>> GetCreatedForPlaylistsAsync(string userName, CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult(Summaries);
        }

        public async Task<LbPlaylistDetail> GetPlaylistAsync(Guid playlistMbid, CancellationToken cancellationToken)
        {
            DetailCalls++;
            if (DelayDetail > TimeSpan.Zero)
            {
                await Task.Delay(DelayDetail, cancellationToken).ConfigureAwait(false);
            }

            if (!Details.TryGetValue(playlistMbid, out var detail))
            {
                throw new ListenBrainzHttpException(
                    $"ListenBrainz playlist {playlistMbid} was not found (it may have expired).",
                    HttpStatusCode.NotFound);
            }

            return detail;
        }
    }

    private sealed class FakeLibraryFacade : ILibraryQueryFacade
    {
        public IReadOnlyList<Audio> Items { get; set; } = [];

        public IReadOnlyList<Audio> GetRecordingTaggedAudioItems(int startIndex, int limit)
            => Items.Skip(startIndex).Take(limit).ToList();

        public IReadOnlyList<Audio> SearchAudioByTitle(string title, int limit)
            => Items.Where(i => i.Name?.Contains(title, StringComparison.OrdinalIgnoreCase) == true)
                .Take(limit)
                .ToList();
    }

    private sealed class FakePlaylistWriter : IPlaylistWriter
    {
        public int WriteCalls { get; private set; }

        public IReadOnlyList<Guid>? LastItemIds { get; private set; }

        public List<string> WrittenNames { get; } = [];

        public Dictionary<string, IReadOnlyList<Guid>> ItemsByName { get; } = [];

        public Dictionary<string, Guid> PlaylistIdsByName { get; } = [];

        public Task<Guid> WriteAsync(Guid? existingPlaylistId, IReadOnlyList<Guid> itemIds, string playlistName, SyncOptions options, CancellationToken cancellationToken)
        {
            WriteCalls++;
            WrittenNames.Add(playlistName);
            LastItemIds = itemIds;
            ItemsByName[playlistName] = itemIds;
            var playlistId = existingPlaylistId ?? Guid.NewGuid();
            PlaylistIdsByName[playlistName] = playlistId;
            return Task.FromResult(playlistId);
        }

        public void Reset()
        {
            WriteCalls = 0;
            LastItemIds = null;
            WrittenNames.Clear();
            ItemsByName.Clear();
            PlaylistIdsByName.Clear();
        }
    }
}
