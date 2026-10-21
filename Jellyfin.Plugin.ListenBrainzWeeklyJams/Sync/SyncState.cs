using System;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// Persisted sync state (design D-9): the last-synced ListenBrainz playlist
/// MBID is the change-detection identity (D-8), the Jellyfin playlist ID is
/// the remembered target (FR-16).
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
}
