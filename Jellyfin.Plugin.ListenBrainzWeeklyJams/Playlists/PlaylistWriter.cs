using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Playlists;

/// <summary>
/// Creates or replaces the target playlist via <see cref="IPlaylistManager"/>
/// (FR-16..20). Replace semantics verified against Jellyfin v10.11.9
/// (Emby.Server.Implementations/Playlists/PlaylistManager.cs L589):
/// <see cref="PlaylistUpdateRequest.Ids"/> replaces the item list and a null
/// <see cref="PlaylistUpdateRequest.Name"/> preserves user renames (D-12).
/// </summary>
public sealed class PlaylistWriter : IPlaylistWriter
{
    private readonly IPlaylistManager _playlistManager;
    private readonly IPlaylistLookup _playlistLookup;
    private readonly IUserManager _userManager;
    private readonly ILogger<PlaylistWriter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistWriter"/> class.
    /// </summary>
    /// <param name="playlistManager">The playlist manager.</param>
    /// <param name="playlistLookup">The playlist lookup.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="logger">The logger.</param>
    public PlaylistWriter(
        IPlaylistManager playlistManager,
        IPlaylistLookup playlistLookup,
        IUserManager userManager,
        ILogger<PlaylistWriter> logger)
    {
        _playlistManager = playlistManager;
        _playlistLookup = playlistLookup;
        _userManager = userManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> WriteAsync(
        Guid? existingPlaylistId,
        IReadOnlyList<Guid> itemIds,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        var ownerUserId = options.OwningUserId ?? GetFirstAdminUserId()
            ?? throw new InvalidOperationException("No Jellyfin user is configured or available to own the playlist.");

        var existing = existingPlaylistId is null ? null : _playlistLookup.GetPlaylist(existingPlaylistId.Value);
        if (existing is null)
        {
            var result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = options.PlaylistName,
                ItemIdList = itemIds,
                MediaType = MediaType.Audio,
                UserId = ownerUserId,
                Public = options.IsPublic
            }).ConfigureAwait(false);

            _logger.LogInformation(
                "Created playlist {Name} with {Count} items for user {UserId}",
                options.PlaylistName,
                itemIds.Count,
                ownerUserId);

            return Guid.Parse(result.Id);
        }

        // One-call replace; Name is deliberately left null so user renames
        // survive (AC-2.2, D-12).
        await _playlistManager.UpdatePlaylist(new PlaylistUpdateRequest
        {
            Id = existing.Id,
            UserId = ownerUserId,
            Ids = itemIds
        }).ConfigureAwait(false);

        _logger.LogInformation(
            "Updated playlist {Name} with {Count} items",
            existing.Name,
            itemIds.Count);

        return existing.Id;
    }

    private Guid? GetFirstAdminUserId()
        => _userManager.GetUsers()
            .FirstOrDefault(u => u.HasPermission(PermissionKind.IsAdministrator))
            ?.Id;
}
