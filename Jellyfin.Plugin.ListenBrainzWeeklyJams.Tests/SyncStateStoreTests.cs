using System;
using System.IO;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Tests;

public sealed class SyncStateStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lbwj-state-{Guid.NewGuid():N}.json");

    [Fact]
    public void MissingFile_LoadsFreshState()
    {
        var store = new SyncStateStore(_path, NullLogger<SyncStateStore>.Instance);

        var state = store.Load();

        Assert.Null(state.LastSyncedPlaylistMbid);
        Assert.Null(state.JellyfinPlaylistId);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new SyncStateStore(_path, NullLogger<SyncStateStore>.Instance);
        var mbid = Guid.Parse("e3d029d6-d8bc-4443-ad64-79fc442c1088");
        var playlistId = Guid.NewGuid();

        store.Save(new SyncState
        {
            LastSyncedPlaylistMbid = mbid,
            LastSyncedWeek = "2026-10-12",
            LastSyncAtUtc = DateTimeOffset.UtcNow,
            JellyfinPlaylistId = playlistId,
            LastSummary = new SyncStateSummary(SyncOutcome.Updated, 50, 48, 45, 3, 2, 11)
        });

        var loaded = store.Load();

        Assert.Equal(mbid, loaded.LastSyncedPlaylistMbid);
        Assert.Equal("2026-10-12", loaded.LastSyncedWeek);
        Assert.Equal(playlistId, loaded.JellyfinPlaylistId);
        Assert.NotNull(loaded.LastSyncAtUtc);
        var summary = Assert.IsType<SyncStateSummary>(loaded.LastSummary);
        Assert.Equal(SyncOutcome.Updated, summary.Outcome);
        Assert.Equal(48, summary.Matched);
    }

    [Fact]
    public void CorruptFile_LoadsFreshState()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not json");

        var store = new SyncStateStore(_path, NullLogger<SyncStateStore>.Instance);

        var state = store.Load();

        Assert.Null(state.LastSyncedPlaylistMbid);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
