using System.Net;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// An unexpected HTTP status from the ListenBrainz API.
/// </summary>
internal sealed class ListenBrainzHttpException : ListenBrainzException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListenBrainzHttpException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    public ListenBrainzHttpException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the HTTP status code.
    /// </summary>
    public HttpStatusCode StatusCode { get; }
}
