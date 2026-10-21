namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// Compact per-sync summary persisted in the state file (FR-24) and served
/// to the config page (FR-04).
/// </summary>
/// <param name="Outcome">The sync outcome.</param>
/// <param name="Total">Total tracks.</param>
/// <param name="Matched">Matched tracks.</param>
/// <param name="ViaMbid">Tracks matched via recording MBID.</param>
/// <param name="ViaName">Tracks matched via name fallback.</param>
/// <param name="Missing">Tracks missing from the library.</param>
/// <param name="Ambiguous">Ambiguously resolved tracks.</param>
public sealed record SyncStateSummary(
    SyncOutcome Outcome,
    int Total,
    int Matched,
    int ViaMbid,
    int ViaName,
    int Missing,
    int Ambiguous);
