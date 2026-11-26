using System;
using System.Globalization;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using Xunit;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Tests;

public sealed class WeeklyJamsSelectorTests
{
    [Fact]
    public async Task SelectLatest_FromCatchUpBatch_PicksNewestEntry()
    {
        // The tabjy fixture contains the two created-for-you entries of a
        // ListenBrainz catch-up batch: created 69s apart, both retained.
        // The newest entry is this week's playlist (D-17) — this is what the
        // plugin synced in production.
        var createdFor = TestHttp.Fixture("tabjy-createdfor.json");
        var (client, _) = TestHttp.Create(_ => TestHttp.Json(createdFor));

        var summaries = await client.GetCreatedForPlaylistsAsync("tabjy", CancellationToken.None);
        var selected = WeeklyJamsSelector.SelectLatest(summaries, "tabjy");

        Assert.Equal(Guid.Parse("e3d029d6-d8bc-4443-ad64-79fc442c1088"), selected.Mbid);
        Assert.Equal("weekly-jams", selected.SourcePatch);
    }

    [Fact]
    public async Task SelectPreviousWeek_FromCatchUpBatch_PicksOlderEntry()
    {
        // The older entry of the batch is last week's playlist — the same
        // interpretation the ListenBrainz website uses (D-17), even though
        // the two entries were created 69 seconds apart.
        var createdFor = TestHttp.Fixture("tabjy-createdfor.json");
        var (client, _) = TestHttp.Create(_ => TestHttp.Json(createdFor));

        var summaries = await client.GetCreatedForPlaylistsAsync("tabjy", CancellationToken.None);
        var previous = WeeklyJamsSelector.SelectPreviousWeekFromList(summaries, "tabjy");

        Assert.NotNull(previous);
        Assert.Equal(Guid.Parse("c576fd05-7496-4899-8a15-cfa54e7daeab"), previous.Mbid);
    }

    [Fact]
    public void SelectPreviousWeek_OnlyOneEntry_ReturnsNull()
    {
        var single = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Jams for x, week of 2026-11-23 Mon",
            DateTimeOffset.UtcNow, "weekly-jams");

        var previous = WeeklyJamsSelector.SelectPreviousWeekFromList([single], "x");

        Assert.Null(previous);
    }

    [Fact]
    public void SelectPreviousWeek_UnsortedInput_OrdersByCreationTime()
    {
        var older = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Jams for x, older",
            new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero), "weekly-jams");
        var newer = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Jams for x, newer",
            new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero), "weekly-jams");

        var latest = WeeklyJamsSelector.SelectLatest([older, newer], "x");
        var previous = WeeklyJamsSelector.SelectPreviousWeekFromList([older, newer], "x");

        Assert.Equal(newer.Mbid, latest.Mbid);
        Assert.Equal(older.Mbid, previous!.Mbid);
    }

    [Fact]
    public void SelectPreviousWeek_IgnoresOtherSourcePatches()
    {
        var jams = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Jams for x", DateTimeOffset.UtcNow, "weekly-jams");
        var exploration = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Exploration for x", DateTimeOffset.UtcNow, "weekly-exploration");

        var previous = WeeklyJamsSelector.SelectPreviousWeekFromList([exploration, jams], "x");

        Assert.Null(previous);
    }

    [Fact]
    public void SelectLatest_PicksMaximumCreationDate()
    {
        var older = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Jams for x, week of 2026-10-05 Mon",
            new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero), "weekly-jams");
        var newer = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Jams for x, week of 2026-10-12 Mon",
            new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero), "weekly-jams");

        var selected = WeeklyJamsSelector.SelectLatest([older, newer], "x");

        Assert.Equal(newer.Mbid, selected.Mbid);
    }

    [Fact]
    public void SelectLatest_NoJams_ThrowsFriendlyError()
    {
        var summaries = new[]
        {
            new LbPlaylistSummary(
                Guid.NewGuid(), "Weekly Exploration for x, week of 2026-10-12 Mon",
                DateTimeOffset.UtcNow, "weekly-exploration")
        };

        var exception = Assert.Throws<ListenBrainzException>(
            () => WeeklyJamsSelector.SelectLatest(summaries, "x"));

        Assert.Contains("No Weekly Jams playlist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectLatest_TitleFallback_WhenSourcePatchMissing()
    {
        // Defensive fallback (FR-07): entries without extension metadata are
        // identified by title prefix.
        var exploration = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Exploration for x, week of 2026-10-12 Mon",
            DateTimeOffset.UtcNow, null);
        var jams = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Jams for x, week of 2026-10-12 Mon",
            DateTimeOffset.UtcNow.AddMinutes(-1), null);

        var selected = WeeklyJamsSelector.SelectLatest([exploration, jams], "x");

        Assert.Equal(jams.Mbid, selected.Mbid);
    }

    [Theory]
    [InlineData("2026-10-13T12:32:24+00:00", "2026-10-12")] // Tuesday -> Monday
    [InlineData("2026-10-12T00:15:00+00:00", "2026-10-12")] // Monday -> same day
    [InlineData("2026-10-18T23:59:00+00:00", "2026-10-12")] // Sunday -> previous Monday
    [InlineData("2026-10-19T09:00:00+00:00", "2026-10-19")] // next Monday -> new week
    public void WeekIdentifier_IsMondayOfCreationWeek(string createdAt, string expected)
    {
        Assert.Equal(expected, WeeklyJamsSelector.GetWeekIdentifier(DateTimeOffset.Parse(createdAt, CultureInfo.InvariantCulture)));
    }
}
