namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// Per-week sync summary, used for the previous-week report and state
/// (FR-23/D-17).
/// </summary>
/// <param name="Outcome">What happened for this week.</param>
/// <param name="Week">The week identifier, e.g. 2026-11-16.</param>
/// <param name="Total">Total tracks.</param>
/// <param name="Matched">Matched tracks.</param>
/// <param name="ViaMbid">Tracks matched via recording MBID.</param>
/// <param name="ViaName">Tracks matched via name fallback.</param>
/// <param name="Missing">Tracks missing from the library.</param>
/// <param name="Ambiguous">Ambiguously resolved tracks.</param>
public sealed record WeekSummary(
    SyncOutcome Outcome,
    string Week,
    int Total,
    int Matched,
    int ViaMbid,
    int ViaName,
    int Missing,
    int Ambiguous);
