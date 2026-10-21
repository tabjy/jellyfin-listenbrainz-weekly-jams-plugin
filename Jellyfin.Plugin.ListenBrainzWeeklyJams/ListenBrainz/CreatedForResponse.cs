using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Created-for-you playlist list response (verified shape, no wrapper).
/// </summary>
internal sealed class CreatedForResponse
{
    /// <summary>
    /// Gets or sets the number of entries in this page.
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the page offset.
    /// </summary>
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    /// <summary>
    /// Gets or sets the total number of playlists across all pages.
    /// </summary>
    [JsonPropertyName("playlist_count")]
    public int PlaylistCount { get; set; }

    /// <summary>
    /// Gets or sets the page entries.
    /// </summary>
    [JsonPropertyName("playlists")]
    public List<CreatedForEntry>? Playlists { get; set; }
}
