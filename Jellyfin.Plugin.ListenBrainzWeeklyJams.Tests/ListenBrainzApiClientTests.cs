using System;
using System.Diagnostics;
using System.Net;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using Xunit;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Tests;

public sealed class ListenBrainzApiClientTests
{
    [Fact]
    public async Task CreatedFor_FollowsPaginationAcrossPages()
    {
        var page0 = TestHttp.Fixture("multipage-createdfor-0.json");
        var page25 = TestHttp.Fixture("multipage-createdfor-25.json");

        var (client, handler) = Create(req =>
            TestHttp.Json(req.RequestUri!.Query.Contains("offset=25", StringComparison.Ordinal) ? page25 : page0));

        var summaries = await client.GetCreatedForPlaylistsAsync("9cxndy", CancellationToken.None);

        Assert.Equal(28, summaries.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("offset=0", handler.Requests[0].Url.Query, StringComparison.Ordinal);
        Assert.Contains("offset=25", handler.Requests[1].Url.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requests_CarryPluginUserAgent()
    {
        var (client, handler) = Create(_ => TestHttp.Json("{}"));

        await client.GetCreatedForPlaylistsAsync("someone", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.StartsWith("Jellyfin.Plugin.ListenBrainzWeeklyJams/", request.UserAgent, StringComparison.Ordinal);
        Assert.Contains("https://github.com/tabjy/", request.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaylistDetail_MapsTracksInOrder()
    {
        var detail = TestHttp.Fixture("tabjy-playlist-detail.json");
        var (client, _) = Create(_ => TestHttp.Json(detail));

        var playlist = await client.GetPlaylistAsync(
            Guid.Parse("e3d029d6-d8bc-4443-ad64-79fc442c1088"),
            CancellationToken.None);

        Assert.Equal("Weekly Jams for tabjy, week of 2026-10-12 Mon", playlist.Title);
        Assert.Equal(50, playlist.Tracks.Count);

        Assert.Equal(1, playlist.Tracks[0].Position);
        Assert.Equal("ローリンガール", playlist.Tracks[0].Title);
        Assert.Equal("wowaka", playlist.Tracks[0].Artist);
        Assert.Equal("SEVEN GIRLS' DISCORD", playlist.Tracks[0].Album);
        Assert.Equal("71d6ceac-9589-4235-a419-6d06e29429d1", playlist.Tracks[0].RecordingMbid);
        Assert.NotEmpty(playlist.Tracks[0].ArtistMbids);

        Assert.Equal(50, playlist.Tracks[^1].Position);
        Assert.Equal("MUSIC BOOK", playlist.Tracks[^1].Title);
        Assert.Equal("山下達郎", playlist.Tracks[^1].Artist);
    }

    [Fact]
    public async Task CreatedFor_UnknownUser_ThrowsUserNotFound()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<ListenBrainzUserNotFoundException>(
            () => client.GetCreatedForPlaylistsAsync("ghost", CancellationToken.None));

        Assert.Contains("ghost", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaylistDetail_NotFound_ThrowsFriendlyError()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<ListenBrainzException>(
            () => client.GetPlaylistAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TooManyRequests_RetriesAndSucceeds()
    {
        var detail = TestHttp.Fixture("tabjy-playlist-detail.json");
        var calls = 0;
        var (client, handler) = Create(_ =>
        {
            calls++;
            if (calls == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.Add("X-RateLimit-Reset-In", "0");
                return response;
            }

            return TestHttp.Json(detail);
        });

        var playlist = await client.GetPlaylistAsync(
            Guid.Parse("e3d029d6-d8bc-4443-ad64-79fc442c1088"),
            CancellationToken.None);

        Assert.Equal(50, playlist.Tracks.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ServerError_RetriesAndSucceeds()
    {
        var detail = TestHttp.Fixture("tabjy-playlist-detail.json");
        var calls = 0;
        var (client, handler) = Create(_ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : TestHttp.Json(detail));

        var playlist = await client.GetPlaylistAsync(
            Guid.Parse("e3d029d6-d8bc-4443-ad64-79fc442c1088"),
            CancellationToken.None);

        Assert.Equal(50, playlist.Tracks.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Unreachable_FailsAfterRetriesWithActionableMessage()
    {
        var (client, handler) = Create(_ => throw new HttpRequestException("connection refused"));

        var exception = await Assert.ThrowsAsync<ListenBrainzException>(
            () => client.GetCreatedForPlaylistsAsync("someone", CancellationToken.None));

        Assert.Contains("unreachable", exception.Message, StringComparison.Ordinal);
        Assert.Equal(4, handler.Requests.Count); // initial + MaxHttpRetries
    }

    [Fact]
    public async Task SequentialRequests_RespectMinimumSpacing()
    {
        var (client, handler) = Create(_ => TestHttp.Json("{}"));
        client.MinRequestInterval = TimeSpan.FromMilliseconds(150);

        var stopwatch = Stopwatch.StartNew();
        await client.GetCreatedForPlaylistsAsync("someone", CancellationToken.None);
        await client.GetCreatedForPlaylistsAsync("someone", CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(2, handler.Requests.Count);
        // Task.Delay timer granularity can fire ~1ms early; the assertion
        // only needs to catch a missing limiter (which would show ~0ms).
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(140),
            $"elapsed only {stopwatch.ElapsedMilliseconds}ms");
    }

    private static (ListenBrainzApiClient Client, FakeHttpMessageHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        => TestHttp.Create(responder);
}
