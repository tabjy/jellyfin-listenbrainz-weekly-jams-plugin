using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Selects the latest Weekly Jams playlist from the created-for-you list
/// using structured metadata only (FR-07): filter on
/// <c>algorithm_metadata.source_patch == "weekly-jams"</c>, then pick the
/// maximum creation date. Title-prefix matching is a defensive fallback for
/// entries without extension metadata; week dates are never parsed from titles.
/// </summary>
public static class WeeklyJamsSelector
{
    /// <summary>The source_patch value identifying Weekly Jams playlists.</summary>
    public const string WeeklyJamsSourcePatch = "weekly-jams";

    /// <summary>The defensive title prefix for Weekly Jams playlists.</summary>
    public const string WeeklyJamsTitlePrefix = "Weekly Jams for ";

    /// <summary>
    /// Selects the latest Weekly Jams playlist.
    /// </summary>
    /// <param name="playlists">The created-for-you playlists.</param>
    /// <param name="userName">The ListenBrainz username, for error messages.</param>
    /// <returns>The latest Weekly Jams playlist summary.</returns>
    /// <exception cref="ListenBrainzException">No Weekly Jams playlist exists.</exception>
    public static LbPlaylistSummary SelectLatest(IEnumerable<LbPlaylistSummary> playlists, string userName)
    {
        var jams = playlists.Where(IsWeeklyJams).ToList();
        if (jams.Count == 0)
        {
            throw new ListenBrainzException(
                $"No Weekly Jams playlist found for '{userName}' yet. A new account, or not enough listening history?");
        }

        // MaxBy returns the first maximum; the created-for-you list is ordered
        // newest-first (verified), and same-week regenerations carry distinct
        // timestamps, so ties in practice do not occur.
        return jams.MaxBy(p => p.CreatedAt)
            ?? throw new ListenBrainzException(
                $"No Weekly Jams playlist found for '{userName}' yet. A new account, or not enough listening history?");
    }

    private static bool IsWeeklyJams(LbPlaylistSummary playlist)
        => string.Equals(playlist.SourcePatch, WeeklyJamsSourcePatch, StringComparison.OrdinalIgnoreCase)
           || (playlist.SourcePatch is null
               && playlist.Title.StartsWith(WeeklyJamsTitlePrefix, StringComparison.OrdinalIgnoreCase));
}
