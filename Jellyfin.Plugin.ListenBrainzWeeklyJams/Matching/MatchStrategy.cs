namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;

/// <summary>
/// How a track was resolved.
/// </summary>
public enum MatchStrategy
{
    /// <summary>The track was not found in the library.</summary>
    None,

    /// <summary>Resolved via MusicBrainz recording MBID (FR-12).</summary>
    Mbid,

    /// <summary>Resolved via exact normalized artist and title (FR-13, T1).</summary>
    NameExact,

    /// <summary>Resolved via bracket-stripped or artist-containment matching (FR-13, T2/T3).</summary>
    NameLoose
}
