using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// JSPF playlist with MusicBrainz extensions.
/// </summary>
internal sealed class JspfPlaylist
{
    /// <summary>
    /// Gets or sets the playlist title.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the playlist creator.
    /// </summary>
    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp (ISO 8601).
    /// </summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>
    /// Gets or sets the playlist identifier URL.
    /// </summary>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; set; }

    /// <summary>
    /// Gets or sets the tracks.
    /// </summary>
    [JsonPropertyName("track")]
    public List<JspfTrack>? Track { get; set; }

    /// <summary>
    /// Gets or sets extension objects keyed by JSPF extension namespace.
    /// </summary>
    [JsonPropertyName("extension")]
    public Dictionary<string, JspfPlaylistExtension>? Extension { get; set; }
}
