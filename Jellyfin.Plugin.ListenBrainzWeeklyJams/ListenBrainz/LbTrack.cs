using System.Collections.Generic;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// An ordered track of a ListenBrainz playlist.
/// </summary>
/// <param name="Position">The 1-based position in the playlist.</param>
/// <param name="RecordingMbid">The MusicBrainz recording MBID; empty if absent.</param>
/// <param name="Title">The track title.</param>
/// <param name="Artist">The track artist credit string.</param>
/// <param name="Album">The release title.</param>
/// <param name="ArtistMbids">MusicBrainz artist MBIDs of the credited artists.</param>
public sealed record LbTrack(
    int Position,
    string RecordingMbid,
    string Title,
    string Artist,
    string Album,
    IReadOnlyList<string> ArtistMbids);
