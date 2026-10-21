using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using Xunit;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Tests;

public sealed class WeeklyJamsSelectorTests
{
    [Fact]
    public async Task SelectLatest_PicksNewestAcrossSameWeekRegeneration()
    {
        // The tabjy fixture contains two weekly-jams playlists for the same
        // week, created 69s apart (a ListenBrainz regeneration). The one with
        // the later timestamp must win (FR-07; verified in the dry run).
        var createdFor = TestHttp.Fixture("tabjy-createdfor.json");
        var (client, _) = TestHttp.Create(_ => TestHttp.Json(createdFor));

        var summaries = await client.GetCreatedForPlaylistsAsync("tabjy", CancellationToken.None);
        var selected = WeeklyJamsSelector.SelectLatest(summaries, "tabjy");

        Assert.Equal(Guid.Parse("e3d029d6-d8bc-4443-ad64-79fc442c1088"), selected.Mbid);
        Assert.Equal("weekly-jams", selected.SourcePatch);
        Assert.Equal("Weekly Jams for tabjy, week of 2026-10-12 Mon", selected.Title);
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
        // identified by title prefix, never by parsing week dates from titles.
        var exploration = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Exploration for x, week of 2026-10-12 Mon",
            DateTimeOffset.UtcNow, null);
        var jams = new LbPlaylistSummary(
            Guid.NewGuid(), "Weekly Jams for x, week of 2026-10-12 Mon",
            DateTimeOffset.UtcNow.AddMinutes(-1), null);

        var selected = WeeklyJamsSelector.SelectLatest([exploration, jams], "x");

        Assert.Equal(jams.Mbid, selected.Mbid);
    }
}
