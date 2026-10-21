using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Playlist detail response (verified shape, no wrapper).
/// </summary>
internal sealed class PlaylistDetailResponse
{
    /// <summary>
    /// Gets or sets the playlist.
    /// </summary>
    [JsonPropertyName("playlist")]
    public JspfPlaylist? Playlist { get; set; }
}
