using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Playlists;

/// <summary>
/// Playlist lookup over <see cref="ILibraryManager"/>.
/// </summary>
public sealed class PlaylistLookup : IPlaylistLookup
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistLookup"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public PlaylistLookup(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public Playlist? GetPlaylist(Guid playlistId)
        => _libraryManager.GetItemById(playlistId) as Playlist;
}
