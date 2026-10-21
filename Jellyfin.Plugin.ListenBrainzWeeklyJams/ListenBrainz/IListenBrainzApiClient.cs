using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// ListenBrainz API surface used by the sync pipeline. Extracted for
/// orchestrator testability (network-bound dependency).
/// </summary>
public interface IListenBrainzApiClient
{
    /// <summary>
    /// Fetches all created-for-you playlists for a user, following pagination (FR-06).
    /// </summary>
    /// <param name="userName">The ListenBrainz username.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The playlist summaries.</returns>
    Task<IReadOnlyList<LbPlaylistSummary>> GetCreatedForPlaylistsAsync(string userName, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a playlist's detail with ordered tracks (FR-08).
    /// </summary>
    /// <param name="playlistMbid">The ListenBrainz playlist MBID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The playlist detail.</returns>
    Task<LbPlaylistDetail> GetPlaylistAsync(Guid playlistMbid, CancellationToken cancellationToken);
}
