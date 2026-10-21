using System;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// A ListenBrainz API failure with a user-facing message (FR-11).
/// </summary>
public class ListenBrainzException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListenBrainzException"/> class.
    /// </summary>
    /// <param name="message">The user-facing error message.</param>
    public ListenBrainzException(string message)
        : base(message)
    {
    }
}
