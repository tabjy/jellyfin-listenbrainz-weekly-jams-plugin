using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Algorithm metadata.
/// </summary>
internal sealed class JspfAlgorithmMetadata
{
    /// <summary>
    /// Gets or sets the generating algorithm, e.g. <c>weekly-jams</c>.
    /// </summary>
    [JsonPropertyName("source_patch")]
    public string? SourcePatch { get; set; }
}
