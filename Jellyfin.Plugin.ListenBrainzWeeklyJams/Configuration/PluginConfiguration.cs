using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ListenBrainzUsername = string.Empty;
        PlaylistName = "Weekly Jams";
    }

    /// <summary>
    /// Gets or sets the ListenBrainz username whose Weekly Jams playlist is synced.
    /// </summary>
    public string ListenBrainzUsername { get; set; }

    /// <summary>
    /// Gets or sets the name of the Jellyfin playlist to keep in sync.
    /// </summary>
    public string PlaylistName { get; set; }

    /// <summary>
    /// Gets or sets the owning Jellyfin user; null means the first
    /// administrator (FR-03, OQ-4).
    /// </summary>
    public Guid? OwningUserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the playlist is visible to
    /// all users (FR-03).
    /// </summary>
    public bool IsPublic { get; set; } = true;
}
