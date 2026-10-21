using System;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// Sync inputs, mapped from plugin configuration at call time. The
/// orchestrator stays configuration-source-agnostic and testable.
/// </summary>
/// <param name="ListenBrainzUsername">The ListenBrainz username (FR-01).</param>
/// <param name="PlaylistName">The target Jellyfin playlist name (FR-02).</param>
/// <param name="OwningUserId">The owning Jellyfin user; null = first admin (FR-03).</param>
/// <param name="IsPublic">Whether the playlist is visible to all users (FR-03).</param>
public sealed record SyncOptions(
    string ListenBrainzUsername,
    string PlaylistName,
    Guid? OwningUserId,
    bool IsPublic);
