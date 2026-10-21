using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// A created-for-you list entry.
/// </summary>
internal sealed class CreatedForEntry
{
    /// <summary>
    /// Gets or sets the playlist.
    /// </summary>
    [JsonPropertyName("playlist")]
    public JspfPlaylist? Playlist { get; set; }
}
