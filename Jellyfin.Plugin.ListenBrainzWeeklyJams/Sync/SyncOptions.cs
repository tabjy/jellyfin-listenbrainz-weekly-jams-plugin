using System;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// Sync inputs, mapped from plugin configuration at call time. The
/// orchestrator stays configuration-source-agnostic and testable.
/// </summary>
/// <param name="ListenBrainzUsername">The ListenBrainz username (FR-01).</param>
/// <param name="PlaylistName">The target Jellyfin playlist name (FR-02).</param>
/// <param name="OwningUserId">The owning Jellyfin user; null = first admin (FR-03).</param>
/// <param name="IsPublic">Whether the playlists are visible to all users (FR-03).</param>
/// <param name="SyncLastWeek">Whether to also maintain a playlist for the
/// previous synced week (D-17, FR-26).</param>
/// <param name="LastWeekPlaylistName">The previous-week playlist name (FR-26).</param>
public sealed record SyncOptions(
    string ListenBrainzUsername,
    string PlaylistName,
    Guid? OwningUserId,
    bool IsPublic,
    bool SyncLastWeek = false,
    string LastWeekPlaylistName = "Last Week's Jams");
