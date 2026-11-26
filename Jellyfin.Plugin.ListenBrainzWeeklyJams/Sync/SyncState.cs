using System;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// Persisted sync state (design D-9): the last-synced ListenBrainz playlist
/// MBID is the change-detection identity (D-8), the Jellyfin playlist ID is
/// the remembered target (FR-16). With D-17, a second target tracks the
/// previous synced week's playlist.
/// </summary>
public sealed class SyncState
{
    /// <summary>
    /// Gets or sets the last-synced ListenBrainz playlist MBID.
    /// </summary>
    public Guid? LastSyncedPlaylistMbid { get; set; }

    /// <summary>
    /// Gets or sets the last-synced week, e.g. 2026-10-12.
    /// </summary>
    public string? LastSyncedWeek { get; set; }

    /// <summary>
    /// Gets or sets the last successful sync time (UTC).
    /// </summary>
    public DateTimeOffset? LastSyncAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the remembered Jellyfin playlist ID.
    /// </summary>
    public Guid? JellyfinPlaylistId { get; set; }

    /// <summary>
    /// Gets or sets the last sync summary.
    /// </summary>
    public SyncStateSummary? LastSummary { get; set; }

    /// <summary>
    /// Gets or sets the ListenBrainz MBID held by the previous-week playlist
    /// (D-17): the second-newest created-for-you entry at the last sync —
    /// the same interpretation of "last week" the ListenBrainz website uses.
    /// </summary>
    public Guid? PreviousSyncedPlaylistMbid { get; set; }

    /// <summary>
    /// Gets or sets the remembered Jellyfin playlist ID of the
    /// previous-week playlist.
    /// </summary>
    public Guid? PreviousJellyfinPlaylistId { get; set; }

    /// <summary>
    /// Gets or sets the previous-week sync summary.
    /// </summary>
    public WeekSummary? LastPreviousWeekSummary { get; set; }
}
