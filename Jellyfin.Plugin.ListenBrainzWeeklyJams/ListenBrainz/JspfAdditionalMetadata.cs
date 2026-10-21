using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Additional metadata.
/// </summary>
internal sealed class JspfAdditionalMetadata
{
    /// <summary>
    /// Gets or sets algorithm metadata.
    /// </summary>
    [JsonPropertyName("algorithm_metadata")]
    public JspfAlgorithmMetadata? AlgorithmMetadata { get; set; }
}
