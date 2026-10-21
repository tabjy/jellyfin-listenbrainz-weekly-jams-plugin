using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// JSPF playlist extension.
/// </summary>
internal sealed class JspfPlaylistExtension
{
    /// <summary>
    /// Gets or sets additional algorithm metadata.
    /// </summary>
    [JsonPropertyName("additional_metadata")]
    public JspfAdditionalMetadata? AdditionalMetadata { get; set; }

    /// <summary>
    /// Gets or sets the user the playlist was created for.
    /// </summary>
    [JsonPropertyName("created_for")]
    public string? CreatedFor { get; set; }
}
