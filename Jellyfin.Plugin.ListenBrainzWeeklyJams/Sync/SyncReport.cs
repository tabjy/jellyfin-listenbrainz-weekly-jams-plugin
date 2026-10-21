using System;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// The result of a sync run (FR-23).
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="PlaylistMbid">The ListenBrainz playlist MBID.</param>
/// <param name="PlaylistTitle">The ListenBrainz playlist title.</param>
/// <param name="Week">The synced week, e.g. 2026-10-12.</param>
/// <param name="Total">Total tracks.</param>
/// <param name="Matched">Matched tracks.</param>
/// <param name="ViaMbid">Tracks matched via recording MBID.</param>
/// <param name="ViaName">Tracks matched via name fallback.</param>
/// <param name="Missing">Tracks missing from the library.</param>
/// <param name="Ambiguous">Ambiguously resolved tracks.</param>
/// <param name="Duration">Wall-clock duration.</param>
public sealed record SyncReport(
    SyncOutcome Outcome,
    Guid PlaylistMbid,
    string PlaylistTitle,
    string Week,
    int Total,
    int Matched,
    int ViaMbid,
    int ViaName,
    int Missing,
    int Ambiguous,
    TimeSpan Duration);
