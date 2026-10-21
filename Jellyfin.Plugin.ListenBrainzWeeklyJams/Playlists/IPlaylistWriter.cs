using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Playlists;

/// <summary>
/// Creates or replaces the target playlist (FR-16..20).
/// </summary>
public interface IPlaylistWriter
{
    /// <summary>
    /// Writes the item list to the playlist: creates it when absent,
    /// replaces its contents in place otherwise.
    /// </summary>
    /// <param name="existingPlaylistId">The remembered playlist ID; null or
    /// deleted means create (FR-19).</param>
    /// <param name="itemIds">The ordered item IDs.</param>
    /// <param name="options">The sync options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The playlist ID (to be remembered in state, FR-16).</returns>
    Task<Guid> WriteAsync(
        Guid? existingPlaylistId,
        IReadOnlyList<Guid> itemIds,
        SyncOptions options,
        CancellationToken cancellationToken);
}
