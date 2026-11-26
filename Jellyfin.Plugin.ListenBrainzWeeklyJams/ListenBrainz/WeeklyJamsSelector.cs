using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Selects Weekly Jams playlists from the created-for-you list using
/// structured metadata only (FR-07): filter on
/// <c>algorithm_metadata.source_patch == "weekly-jams"</c>, then order by
/// creation time.
///
/// The created-for-you list retains the two most recent Weekly Jams
/// playlists: the newest entry is this week, the older entry is last week —
/// even when ListenBrainz lags under load and computes both in one catch-up
/// batch seconds apart (observed 2026-11: two playlists created 78 s apart;
/// titles lag too, so they are not parsed). This mirrors the ListenBrainz
/// website's interpretation. New accounts may have a single entry.
/// </summary>
public static class WeeklyJamsSelector
{
    /// <summary>The source_patch value identifying Weekly Jams playlists.</summary>
    public const string WeeklyJamsSourcePatch = "weekly-jams";

    /// <summary>The defensive title prefix for Weekly Jams playlists.</summary>
    public const string WeeklyJamsTitlePrefix = "Weekly Jams for ";

    /// <summary>
    /// Selects this week's Weekly Jams playlist: the newest created-for-you
    /// entry.
    /// </summary>
    /// <param name="playlists">The created-for-you playlists.</param>
    /// <param name="userName">The ListenBrainz username, for error messages.</param>
    /// <returns>This week's playlist summary.</returns>
    /// <exception cref="ListenBrainzException">No Weekly Jams playlist exists.</exception>
    public static LbPlaylistSummary SelectLatest(IEnumerable<LbPlaylistSummary> playlists, string userName)
    {
        var weekly = WeeklyJamsDescending(playlists).ToList();
        if (weekly.Count == 0)
        {
            throw new ListenBrainzException(
                $"No Weekly Jams playlist found for '{userName}' yet. A new account, or not enough listening history?");
        }

        return weekly[0];
    }

    /// <summary>
    /// Selects last week's Weekly Jams playlist: the second-newest
    /// created-for-you entry. Under catch-up batches the two entries are
    /// created seconds apart, but they still cover two distinct weeks of
    /// listening history (D-17).
    /// </summary>
    /// <param name="playlists">The created-for-you playlists.</param>
    /// <param name="userName">The ListenBrainz username, for error messages.</param>
    /// <returns>Last week's playlist summary, or null when only one entry
    /// exists (new accounts).</returns>
    public static LbPlaylistSummary? SelectPreviousWeekFromList(
        IEnumerable<LbPlaylistSummary> playlists,
        string userName)
    {
        var weekly = WeeklyJamsDescending(playlists).ToList();
        return weekly.Count >= 2 ? weekly[1] : null;
    }

    /// <summary>
    /// Derives the week identifier (the Monday of the playlist's creation
    /// week) for display purposes. Note that under ListenBrainz catch-up
    /// load the creation time lags the covered week; playlist titles lag as
    /// well, so neither is a reliable covered-week label — the identifier is
    /// cosmetic only (change detection uses playlist MBIDs, D-8).
    /// </summary>
    /// <param name="createdAt">The playlist creation time.</param>
    /// <returns>The week identifier, e.g. 2026-10-12.</returns>
    public static string GetWeekIdentifier(DateTimeOffset createdAt)
    {
        var daysFromMonday = ((int)createdAt.DayOfWeek + 6) % 7;
        return createdAt.AddDays(-daysFromMonday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<LbPlaylistSummary> WeeklyJamsDescending(
        IEnumerable<LbPlaylistSummary> playlists)
        => playlists
            .Where(IsWeeklyJams)
            .OrderByDescending(p => p.CreatedAt);

    private static bool IsWeeklyJams(LbPlaylistSummary playlist)
        => string.Equals(playlist.SourcePatch, WeeklyJamsSourcePatch, StringComparison.OrdinalIgnoreCase)
           || (playlist.SourcePatch is null
               && playlist.Title.StartsWith(WeeklyJamsTitlePrefix, StringComparison.OrdinalIgnoreCase));
}
