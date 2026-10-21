using System;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Summary of a ListenBrainz created-for-you playlist.
/// </summary>
/// <param name="Mbid">The ListenBrainz playlist identifier.</param>
/// <param name="Title">The playlist title.</param>
/// <param name="CreatedAt">The playlist creation time.</param>
/// <param name="SourcePatch">The generating algorithm, e.g. <c>weekly-jams</c>; null if metadata absent.</param>
public sealed record LbPlaylistSummary(
    Guid Mbid,
    string Title,
    DateTimeOffset CreatedAt,
    string? SourcePatch);
