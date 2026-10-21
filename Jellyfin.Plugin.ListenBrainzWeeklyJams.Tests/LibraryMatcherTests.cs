using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Tests;

public sealed class LibraryMatcherTests
{
    private const string MbId = "11111111-1111-1111-1111-111111111111";
    private const string DupMbid = "55555555-5555-5555-5555-555555555555";
    private const string ArtistMbid = "aaaa0000-0000-0000-0000-000000000001";

    private readonly FakeFacade _facade = new();
    private readonly LibraryMatcher _matcher;

    public LibraryMatcherTests()
    {
        _matcher = new LibraryMatcher(_facade, NullLogger<LibraryMatcher>.Instance)
        {
            PageSize = 2
        };
    }

    [Fact]
    public void Mbid_ExactMatch()
    {
        var item = Item("item-1", "Track One", ["Artist A"], ProviderIds(MbId, ArtistMbid));
        _facade.Items = [item];

        var report = _matcher.Resolve([Track(MbId, "Track One", "Artist A")]);

        var match = Assert.Single(report.Matches);
        Assert.Equal(MatchStrategy.Mbid, match.Strategy);
        Assert.Same(item, match.Item);
        Assert.False(match.Ambiguous);
        Assert.Equal([item.Id], report.OrderedItemIds);
        Assert.Equal(0, _facade.TitleSearchCalls);
    }

    [Fact]
    public void Mbid_Ambiguity_PicksLowestIdAndFlags()
    {
        var low = Item(new Guid("00000000-0000-0000-0000-000000000001"), "Track Five", ["Artist E"], ProviderIds(DupMbid));
        var high = Item(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), "Track Five", ["Artist E"], ProviderIds(DupMbid));
        _facade.Items = [high, low];

        var report = _matcher.Resolve([Track(DupMbid, "Track Five", "Artist E")]);

        var match = Assert.Single(report.Matches);
        Assert.True(match.Ambiguous);
        Assert.Equal(1, report.AmbiguousCount);
        Assert.Equal(low.Id, match.Item!.Id);
    }

    [Fact]
    public void Oq6_ExactTitleBeatsIdOrder()
    {
        // The candidate with the exact title wins even though its Id sorts later.
        var live = Item(new Guid("00000000-0000-0000-0000-000000000001"), "Track (Live)", ["Artist A"], ProviderIds(MbId));
        var exact = Item(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), "Track", ["Artist A"], ProviderIds(MbId));
        _facade.Items = [live, exact];

        var report = _matcher.Resolve([Track(MbId, "Track", "Artist A")]);

        Assert.Equal(exact.Id, report.Matches[0].Item!.Id);
    }

    [Fact]
    public void Mbid_PagedBulk_StreamsAndFilters()
    {
        // Five items, page size two -> three paged calls; only the two
        // playlist MBIDs survive the streaming filter.
        var target1 = Item("t1", "Track One", ["Artist A"], ProviderIds(MbId));
        var target2 = Item("t2", "Track Two", ["Artist B"], ProviderIds(DupMbid));
        _facade.Items =
        [
            Item("d1", "Filler One", ["X"], []),
            target1,
            Item("d2", "Filler Two", ["Y"], []),
            Item("d3", "Filler Three", ["Z"], []),
            target2,
        ];

        var report = _matcher.Resolve(
        [
            Track(MbId, "Track One", "Artist A"),
            Track(DupMbid, "Track Two", "Artist B"),
        ]);

        Assert.Equal(2, report.MatchedCount);
        Assert.Equal([target1.Id, target2.Id], report.OrderedItemIds);
        Assert.Equal(0, _facade.TitleSearchCalls);

        // (0,2), (2,2), (4,2)
        Assert.Equal(3, _facade.PagedCalls.Count);
        Assert.Equal((0, 2), _facade.PagedCalls[0]);
        Assert.Equal((2, 2), _facade.PagedCalls[1]);
        Assert.Equal((4, 2), _facade.PagedCalls[2]);
    }

    [Fact]
    public void Name_T1_ExactNormalized()
    {
        _facade.Items = [Item("item-2", "Track Two", ["Artist B"], [])];

        var report = _matcher.Resolve([Track(string.Empty, "track two", "ARTIST B")]);

        Assert.Equal(MatchStrategy.NameExact, report.Matches[0].Strategy);
        Assert.NotNull(report.Matches[0].Item);
    }

    [Fact]
    public void Name_T2_BracketStripping()
    {
        _facade.Items = [Item("item-3", "Track Three (Remastered 2011)", ["Artist C"], [])];

        var report = _matcher.Resolve([Track(string.Empty, "Track Three", "Artist C")]);

        Assert.Equal(MatchStrategy.NameLoose, report.Matches[0].Strategy);
        Assert.NotNull(report.Matches[0].Item);
    }

    [Fact]
    public void Name_T3_ArtistContainment()
    {
        // "The Beatles" (ListenBrainz) vs "Beatles" (library): article difference only.
        _facade.Items = [Item("item-4", "Yesterday", ["Beatles"], [])];

        var report = _matcher.Resolve([Track(string.Empty, "Yesterday", "The Beatles")]);

        Assert.Equal(MatchStrategy.NameLoose, report.Matches[0].Strategy);
        Assert.NotNull(report.Matches[0].Item);
    }

    [Fact]
    public void Name_FeatCollapsesToT1()
    {
        // norm() strips "feat. ..." so this resolves at the exact tier, not T3.
        _facade.Items = [Item("item-2", "Track Two", ["Artist B"], [])];

        var report = _matcher.Resolve([Track(string.Empty, "Track Two", "Artist B feat. Someone")]);

        Assert.Equal(MatchStrategy.NameExact, report.Matches[0].Strategy);
        Assert.NotNull(report.Matches[0].Item);
    }

    [Fact]
    public void Name_ArtistMbidTiebreak()
    {
        var cover = Item("cover", "Same Song", ["Cover Band"], []);
        var original = Item(
            "orig",
            "Same Song",
            ["Orig Artist"],
            new Dictionary<string, string> { [LibraryMatcher.ArtistProviderId] = ArtistMbid });
        _facade.Items = [cover, original];

        var report = _matcher.Resolve(
        [
            Track(string.Empty, "Same Song", "Orig Artist", artistMbids: [ArtistMbid])
        ]);

        Assert.Equal(original.Id, report.Matches[0].Item!.Id);
    }

    [Fact]
    public void Miss_IsReported()
    {
        _facade.Items = [Item("item-1", "Track One", ["Artist A"], [])];

        var track = Track(string.Empty, "No Such Song", "Nobody");
        var report = _matcher.Resolve([track]);

        var match = Assert.Single(report.Matches);
        Assert.Equal(MatchStrategy.None, match.Strategy);
        Assert.Null(match.Item);
        Assert.Equal(1, report.MissingCount);
        Assert.Same(track, Assert.Single(report.MissingTracks));
        Assert.Empty(report.OrderedItemIds);
    }

    [Fact]
    public void OrderedItemIds_PreserveOrderAndDedupe()
    {
        var shared = Item("shared", "Track Two", ["Artist B"], []);
        _facade.Items =
        [
            Item("a", "Track One", ["Artist A"], []),
            shared,
        ];

        var report = _matcher.Resolve(
        [
            Track(string.Empty, "Track One", "Artist A"),
            Track(string.Empty, "Track Two", "Artist B"),
            Track(string.Empty, "TRACK TWO", "artist b"), // resolves to the same item
        ]);

        Assert.Equal(3, report.MatchedCount);
        Assert.Equal(2, report.OrderedItemIds.Count); // deduplicated (FR-14)
    }

    private static Dictionary<string, string> ProviderIds(string recordingMbid, string? artistMbid = null)
    {
        var ids = new Dictionary<string, string> { [LibraryMatcher.RecordingProviderId] = recordingMbid };
        if (artistMbid is not null)
        {
            ids[LibraryMatcher.ArtistProviderId] = artistMbid;
        }

        return ids;
    }

    private static LbTrack Track(
        string recordingMbid,
        string title,
        string artist,
        IReadOnlyList<string>? artistMbids = null)
        => new(1, recordingMbid, title, artist, "Some Album", artistMbids ?? []);

    private static Audio Item(string id, string name, string[] artists, Dictionary<string, string> providerIds)
        => Item(Guid.Parse(Md5ish(id)), name, artists, providerIds);

    private static Audio Item(Guid id, string name, string[] artists, Dictionary<string, string> providerIds)
        => new()
        {
            Id = id,
            Name = name,
            Artists = artists,
            ProviderIds = providerIds
        };

    private static string Md5ish(string id)
        => new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(id))).ToString();

    private sealed class FakeFacade : ILibraryQueryFacade
    {
        private readonly List<(int StartIndex, int Limit)> _pagedCalls = [];

        public IReadOnlyList<Audio> Items { get; set; } = [];

        public IReadOnlyList<(int StartIndex, int Limit)> PagedCalls => _pagedCalls;

        public int TitleSearchCalls { get; private set; }

        public IReadOnlyList<Audio> GetRecordingTaggedAudioItems(int startIndex, int limit)
        {
            _pagedCalls.Add((startIndex, limit));
            return Items.Skip(startIndex).Take(limit).ToList();
        }

        public IReadOnlyList<Audio> SearchAudioByTitle(string title, int limit)
        {
            TitleSearchCalls++;
            return Items
                .Where(i => i.Name?.Contains(title, StringComparison.OrdinalIgnoreCase) == true)
                .Take(limit)
                .ToList();
        }
    }
}
