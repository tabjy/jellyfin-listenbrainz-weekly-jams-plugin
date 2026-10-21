using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Additional track metadata.
/// </summary>
internal sealed class JspfTrackAdditionalMetadata
{
    /// <summary>
    /// Gets or sets artist credits.
    /// </summary>
    [JsonPropertyName("artists")]
    public List<JspfArtistCredit>? Artists { get; set; }
}
