using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private static readonly Guid JamsMbid = Guid.Parse("e3d029d6-d8bc-4443-ad64-79fc442c1088");
    private static readonly Guid NewJamsMbid = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"lbwj-orch-{Guid.NewGuid():N}.json");
    private readonly FakeListenBrainzClient _client = new();
    private readonly FakeLibraryFacade _facade = new();
    private readonly FakePlaylistWriter _playlistWriter = new();
    private SyncOrchestrator _orchestrator = null!;

    private static SyncOptions Options { get; } = new("tabjy", "Weekly Jams", OwningUserId: Guid.NewGuid(), IsPublic: true);

    [Fact]
    public async Task FullSync_MatchesWritesAndAdvancesState()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        var item = NewItem(mbid);
        _facade.Items = [item];
        SetupWeek(
            JamsMbid,
            week: 0,
            tracks:
            [
                NewTrack(mbid),
                new LbTrack(2, "ffffffff-ffff-ffff-ffff-ffffffffffff", "Ghost Track", "Ghost Artist", "X", [])
            ]);

        var report = await CreateOrchestrator().SyncAsync(Options, force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(2, report.Total);
        Assert.Equal(1, report.Matched);
        Assert.Equal(1, report.Missing);
        // Created 2026-10-13 (Tuesday) -> week identifier is Monday 2026-10-12.
        Assert.Equal("2026-10-12", report.Week);
        Assert.Equal([item.Id], _playlistWriter.LastItemIds);
        var state = LoadState();
        Assert.Equal(JamsMbid, state.LastSyncedPlaylistMbid);
        Assert.NotNull(state.JellyfinPlaylistId);
        Assert.Equal(SyncOutcome.Updated, state.LastSummary!.Outcome);
    }

    [Theory]
    [InlineData("2026-10-13T12:32:24+00:00", "2026-10-12")] // Tuesday -> Monday
    [InlineData("2026-10-12T00:15:00+00:00", "2026-10-12")] // Monday -> same day
    [InlineData("2026-10-18T23:59:00+00:00", "2026-10-12")] // Sunday -> previous Monday
    [InlineData("2026-10-19T09:00:00+00:00", "2026-10-19")] // next Monday -> new week
    public void WeekIdentifier_IsMondayOfCreationWeek(string createdAt, string expected)
    {
        Assert.Equal(expected, SyncOrchestrator.GetWeekIdentifier(DateTimeOffset.Parse(createdAt, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task SecondRun_ShortCircuitsWithoutDetailFetchOrWrite()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        SetupWeek(JamsMbid, week: 0, tracks: [NewTrack(mbid)]);
        var orchestrator = CreateOrchestrator();

        await orchestrator.SyncAsync(Options, force: false, CancellationToken.None);
        _playlistWriter.Reset();
        _client.ResetCounters();

        var report = await orchestrator.SyncAsync(Options, force: false, CancellationToken.None);

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
        SetupWeek(JamsMbid, week: 0, tracks: [NewTrack(mbid)]);
        var orchestrator = CreateOrchestrator();

        await orchestrator.SyncAsync(Options, force: false, CancellationToken.None);
        _playlistWriter.Reset();
        _client.ResetCounters();

        var report = await orchestrator.SyncAsync(Options, force: true, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(1, _client.DetailCalls);
        Assert.Equal(1, _playlistWriter.WriteCalls);
    }

    [Fact]
    public async Task NewWeek_SyncsAgain()
    {
        var mbid = "71d6ceac-9589-4235-a419-6d06e29429d1";
        _facade.Items = [NewItem(mbid)];
        SetupWeek(JamsMbid, week: 0, tracks: [NewTrack(mbid)]);
        var orchestrator = CreateOrchestrator();
        await orchestrator.SyncAsync(Options, force: false, CancellationToken.None);
        _playlistWriter.Reset();
        _client.ResetCounters();

        // ListenBrainz regenerated the week: a new playlist MBID appears.
        SetupWeek(NewJamsMbid, week: 7, tracks: [NewTrack(mbid)]);

        var report = await orchestrator.SyncAsync(Options, force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Updated, report.Outcome);
        Assert.Equal(1, _playlistWriter.WriteCalls);
        Assert.Equal(NewJamsMbid, LoadState().LastSyncedPlaylistMbid);
    }

    [Fact]
    public async Task ZeroMatches_GuardDoesNotTouchPlaylistOrAdvanceState()
    {
        // The track matches nothing: no MBID candidate, and its title/artist
        // must also dodge the name fallback.
        _facade.Items = [NewItem("71d6ceac-9589-4235-a419-6d06e29429d1")];
        SetupWeek(
            JamsMbid,
            week: 0,
            tracks: [new LbTrack(1, "00000000-0000-0000-0000-0000000000ff", "Ghost Track", "Ghost Artist", "X", [])]);

        var report = await CreateOrchestrator().SyncAsync(Options, force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.ZeroMatchGuard, report.Outcome);
        Assert.Equal(0, _playlistWriter.WriteCalls);
        Assert.Null(LoadState().LastSyncedPlaylistMbid);

        // State was not advanced, so a subsequent run retries the same week
        // (and matches once the library gains the recording).
        _facade.Items = [NewItem("00000000-0000-0000-0000-0000000000ff")];
        _playlistWriter.Reset();
        var retry = await CreateOrchestrator().SyncAsync(Options, force: false, CancellationToken.None);
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
        _facade.Items = [NewItem("71d6ceac-9589-4235-a419-6d06e29429d1")];
        SetupWeek(JamsMbid, week: 0, tracks: [NewTrack("71d6ceac-9589-4235-a419-6d06e29429d1")]);
        _client.DelayDetail = TimeSpan.FromMilliseconds(200);
        var orchestrator = CreateOrchestrator();

        var firstTask = orchestrator.SyncAsync(Options, force: false, CancellationToken.None);
        await Assert.ThrowsAsync<SyncInProgressException>(
            () => orchestrator.SyncAsync(Options, force: false, CancellationToken.None));
        var first = await firstTask;

        Assert.Equal(SyncOutcome.Updated, first.Outcome);
    }

    private SyncOrchestrator CreateOrchestrator()
    {
        _orchestrator = new SyncOrchestrator(
            _client,
            new LibraryMatcher(_facade, NullLogger<LibraryMatcher>.Instance) { PageSize = 100 },
            _playlistWriter,
            new SyncStateStore(_statePath, NullLogger<SyncStateStore>.Instance),
            NullLogger<SyncOrchestrator>.Instance);
        return _orchestrator;
    }

    private SyncState LoadState()
        => new SyncStateStore(_statePath, NullLogger<SyncStateStore>.Instance).Load();

    private void SetupWeek(Guid mbid, int week, IReadOnlyList<LbTrack> tracks)
    {
        _client.Summaries =
        [
            new LbPlaylistSummary(
                mbid,
                $"Weekly Jams for tabjy, week of 2026-10-{12 + week:00} Mon",
                new DateTimeOffset(2026, 10, 13 + week, 12, 32, 24, TimeSpan.Zero),
                "weekly-jams"),
        ];
        _client.Detail = new LbPlaylistDetail(mbid, _client.Summaries[0].Title, _client.Summaries[0].CreatedAt, tracks);
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
        _orchestrator?.Dispose();
        if (File.Exists(_statePath))
        {
            File.Delete(_statePath);
        }
    }

    private sealed class FakeListenBrainzClient : IListenBrainzApiClient
    {
        public IReadOnlyList<LbPlaylistSummary> Summaries { get; set; } = [];

        public LbPlaylistDetail Detail { get; set; } = null!;

        public TimeSpan DelayDetail { get; set; } = TimeSpan.Zero;

        public int ListCalls { get; private set; }

        public int DetailCalls { get; private set; }

        public void ResetCounters()
        {
            ListCalls = 0;
            DetailCalls = 0;
        }

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

            return Detail;
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
        private Guid _nextPlaylistId = Guid.NewGuid();

        public int WriteCalls { get; private set; }

        public IReadOnlyList<Guid>? LastItemIds { get; private set; }

        public Task<Guid> WriteAsync(Guid? existingPlaylistId, IReadOnlyList<Guid> itemIds, SyncOptions options, CancellationToken cancellationToken)
        {
            WriteCalls++;
            LastItemIds = itemIds;
            return Task.FromResult(existingPlaylistId ?? _nextPlaylistId);
        }

        public void Reset()
        {
            WriteCalls = 0;
            LastItemIds = null;
        }
    }
}
