using System;
using MediaBrowser.Controller.Playlists;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Playlists;

/// <summary>
/// Playlist existence lookup seam (small by design, like the library facade,
/// so the writer is testable without faking all of ILibraryManager).
/// </summary>
public interface IPlaylistLookup
{
    /// <summary>
    /// Gets the playlist with the given ID; null when it no longer exists
    /// (FR-19: a deleted playlist is recreated on the next sync).
    /// </summary>
    /// <param name="playlistId">The playlist ID.</param>
    /// <returns>The playlist, or null.</returns>
    Playlist? GetPlaylist(Guid playlistId);
}
