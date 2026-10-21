using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Full detail of a ListenBrainz playlist, with ordered tracks.
/// </summary>
/// <param name="Mbid">The ListenBrainz playlist identifier.</param>
/// <param name="Title">The playlist title.</param>
/// <param name="CreatedAt">The playlist creation time.</param>
/// <param name="Tracks">The ordered tracks.</param>
public sealed record LbPlaylistDetail(
    Guid Mbid,
    string Title,
    DateTimeOffset CreatedAt,
    IReadOnlyList<LbTrack> Tracks);
