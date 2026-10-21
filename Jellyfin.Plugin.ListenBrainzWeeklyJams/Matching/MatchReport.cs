using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;

/// <summary>
/// The result of matching a playlist against the library.
/// </summary>
/// <param name="Matches">Per-track results, in playlist order.</param>
/// <param name="OrderedItemIds">Deduplicated item IDs in playlist order (FR-14).</param>
/// <param name="MissingTracks">Tracks not present in the library, in playlist order.</param>
public sealed record MatchReport(
    IReadOnlyList<TrackMatch> Matches,
    IReadOnlyList<Guid> OrderedItemIds,
    IReadOnlyList<LbTrack> MissingTracks)
{
    /// <summary>
    /// Gets the total number of tracks.
    /// </summary>
    public int Total => Matches.Count;

    /// <summary>
    /// Gets the number of matched tracks.
    /// </summary>
    public int MatchedCount => Matches.Count(m => m.Item is not null);

    /// <summary>
    /// Gets the number of tracks matched via recording MBID.
    /// </summary>
    public int ViaMbidCount => Matches.Count(m => m.Strategy == MatchStrategy.Mbid);

    /// <summary>
    /// Gets the number of tracks matched via name fallback.
    /// </summary>
    public int ViaNameCount => Matches.Count(m => m.Strategy is MatchStrategy.NameExact or MatchStrategy.NameLoose);

    /// <summary>
    /// Gets the number of missing tracks.
    /// </summary>
    public int MissingCount => MissingTracks.Count;

    /// <summary>
    /// Gets the number of ambiguously resolved tracks.
    /// </summary>
    public int AmbiguousCount => Matches.Count(m => m.Ambiguous);
}
