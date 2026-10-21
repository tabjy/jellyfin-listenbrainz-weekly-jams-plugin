namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// The configured ListenBrainz user does not exist.
/// </summary>
public sealed class ListenBrainzUserNotFoundException : ListenBrainzException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListenBrainzUserNotFoundException"/> class.
    /// </summary>
    /// <param name="userName">The ListenBrainz username.</param>
    public ListenBrainzUserNotFoundException(string userName)
        : base($"ListenBrainz user '{userName}' was not found. Check the username in the plugin configuration.")
    {
    }
}
