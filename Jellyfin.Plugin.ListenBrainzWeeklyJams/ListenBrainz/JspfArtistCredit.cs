using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Artist credit.
/// </summary>
internal sealed class JspfArtistCredit
{
    /// <summary>
    /// Gets or sets the artist MBID.
    /// </summary>
    [JsonPropertyName("artist_mbid")]
    public string? ArtistMbid { get; set; }
}
