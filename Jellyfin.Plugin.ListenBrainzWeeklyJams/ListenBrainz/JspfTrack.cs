using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// JSPF track.
/// </summary>
internal sealed class JspfTrack
{
    /// <summary>
    /// Gets or sets the track title.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the artist credit string.
    /// </summary>
    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    /// <summary>
    /// Gets or sets the release title.
    /// </summary>
    [JsonPropertyName("album")]
    public string? Album { get; set; }

    /// <summary>
    /// Gets or sets identifier URLs; the first is the recording MBID URL.
    /// </summary>
    [JsonPropertyName("identifier")]
    public List<string>? Identifier { get; set; }

    /// <summary>
    /// Gets or sets extension objects keyed by JSPF extension namespace.
    /// </summary>
    [JsonPropertyName("extension")]
    public Dictionary<string, JspfTrackExtension>? Extension { get; set; }
}
