using System.Net;
using System.Text;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Tests;

/// <summary>
/// A captured request snapshot (the client disposes the request message).
/// </summary>
/// <param name="Url">The request URL.</param>
/// <param name="UserAgent">The User-Agent header value.</param>
public sealed record CapturedRequest(Uri Url, string? UserAgent);

/// <summary>
/// HTTP handler fake that records requests and answers from a responder.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeHttpMessageHandler"/> class.
    /// </summary>
    /// <param name="responder">Maps a request to a response.</param>
    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>
    /// Gets the captured requests, in order.
    /// </summary>
    public List<CapturedRequest> Requests { get; } = [];

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedRequest(request.RequestUri!, request.Headers.UserAgent.ToString()));
        return Task.FromResult(_responder(request));
    }
}

/// <summary>
/// Factory fake serving a single handler (Microsoft's IHttpClientFactory shape).
/// </summary>
public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeHttpClientFactory"/> class.
    /// </summary>
    /// <param name="handler">The handler all clients use.</param>
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

    /// <inheritdoc />
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

/// <summary>
/// Test helpers.
/// </summary>
public static class TestHttp
{
    /// <summary>
    /// Creates a client over a fake handler with test-friendly retry/interval settings.
    /// </summary>
    /// <param name="responder">Maps a request to a response.</param>
    /// <returns>The client and its handler.</returns>
    public static (ListenBrainzApiClient Client, FakeHttpMessageHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var client = new ListenBrainzApiClient(
            new FakeHttpClientFactory(handler),
            NullLogger<ListenBrainzApiClient>.Instance)
        {
            MinRequestInterval = TimeSpan.FromMilliseconds(5),
            RetryBaseDelay = TimeSpan.FromMilliseconds(5)
        };

        return (client, handler);
    }

    /// <summary>
    /// Reads a test fixture.
    /// </summary>
    /// <param name="name">The fixture file name.</param>
    /// <returns>The fixture content.</returns>
    public static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    /// <summary>
    /// Builds a JSON response.
    /// </summary>
    /// <param name="body">The JSON body.</param>
    /// <returns>The response message.</returns>
    public static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
}
