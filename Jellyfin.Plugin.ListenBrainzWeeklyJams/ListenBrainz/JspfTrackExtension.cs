using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// JSPF track extension.
/// </summary>
internal sealed class JspfTrackExtension
{
    /// <summary>
    /// Gets or sets additional track metadata.
    /// </summary>
    [JsonPropertyName("additional_metadata")]
    public JspfTrackAdditionalMetadata? AdditionalMetadata { get; set; }
}
