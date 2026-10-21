using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using MediaBrowser.Controller.Entities.Audio;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;

/// <summary>
/// The resolution of a single playlist track.
/// </summary>
/// <param name="Track">The ListenBrainz track.</param>
/// <param name="Strategy">How the track was resolved.</param>
/// <param name="Ambiguous">Whether multiple candidates existed (OQ-6 policy applied).</param>
/// <param name="Item">The matched Jellyfin audio item; null when missing.</param>
public sealed record TrackMatch(
    LbTrack Track,
    MatchStrategy Strategy,
    bool Ambiguous,
    Audio? Item);
