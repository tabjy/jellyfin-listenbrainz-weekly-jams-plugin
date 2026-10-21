using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;

/// <summary>
/// Resolves ListenBrainz playlist tracks to Jellyfin audio items (FR-12..15,
/// OQ-5/OQ-6). Strategy: paged bulk over recording-tagged items with a
/// streaming MBID-set filter (memory bounded by page size), then per-miss
/// name fallback with normalized tiers and an artist-MBID tiebreak.
/// </summary>
public sealed class LibraryMatcher
{
    /// <summary>The provider ID key holding the recording MBID.</summary>
    public const string RecordingProviderId = "MusicBrainzRecording";

    /// <summary>The provider ID key holding the artist MBID.</summary>
    public const string ArtistProviderId = "MusicBrainzArtist";

    /// <summary>The default library page size (design §3.5).</summary>
    public const int DefaultPageSize = 5000;

    private const int TitleSearchLimit = 50;

    private readonly ILibraryQueryFacade _facade;
    private readonly ILogger<LibraryMatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryMatcher"/> class.
    /// </summary>
    /// <param name="facade">The library query facade.</param>
    /// <param name="logger">The logger.</param>
    public LibraryMatcher(ILibraryQueryFacade facade, ILogger<LibraryMatcher> logger)
    {
        _facade = facade;
        _logger = logger;
    }

    /// <summary>
    /// Gets or sets the library page size. Exposed for tests.
    /// </summary>
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// Resolves the playlist tracks against the library.
    /// </summary>
    /// <param name="tracks">The ordered playlist tracks.</param>
    /// <param name="progress">Optional progress reporter (0..1).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The match report.</returns>
    public MatchReport Resolve(
        IReadOnlyList<LbTrack> tracks,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = CollectMbidCandidates(tracks, progress, cancellationToken);
        progress?.Report(0.5);

        var matches = new List<TrackMatch>(tracks.Count);
        foreach (var track in tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Audio? item;
            var strategy = MatchStrategy.None;
            var ambiguous = false;

            if (track.RecordingMbid.Length != 0
                && candidates.TryGetValue(track.RecordingMbid, out var pool)
                && pool.Count > 0)
            {
                // FR-12: MBID first.
                strategy = MatchStrategy.Mbid;
                ambiguous = pool.Count > 1;
                item = PickByAmbiguityPolicy(pool, track);
            }
            else
            {
                // FR-13: name fallback, per-miss only.
                var found = _facade.SearchAudioByTitle(track.Title, TitleSearchLimit);
                (item, strategy, ambiguous) = MatchByName(track, found);
            }

            if (item is null)
            {
                // FR-15: unmatched tracks are reported.
                _logger.LogInformation(
                    "Missing from library: {Artist} — {Title} [{Mbid}]",
                    track.Artist,
                    track.Title,
                    track.RecordingMbid.Length == 0 ? "no mbid" : track.RecordingMbid);
            }

            matches.Add(new TrackMatch(track, strategy, ambiguous, item));
        }

        progress?.Report(1.0);

        var report = new MatchReport(
            matches,
            matches.Where(m => m.Item is not null).Select(m => m.Item!.Id).Distinct().ToList(),
            matches.Where(m => m.Item is null).Select(m => m.Track).ToList());

        _logger.LogInformation(
            "Matched {Matched}/{Total} tracks ({Mbid} by MBID, {Name} by name, {Missing} missing, {Ambiguous} ambiguous)",
            report.MatchedCount,
            report.Total,
            report.ViaMbidCount,
            report.ViaNameCount,
            report.MissingCount,
            report.AmbiguousCount);

        return report;
    }

    private Dictionary<string, List<Audio>> CollectMbidCandidates(
        IReadOnlyList<LbTrack> tracks,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var mbidSet = tracks
            .Select(t => t.RecordingMbid)
            .Where(m => m.Length != 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = new Dictionary<string, List<Audio>>(StringComparer.OrdinalIgnoreCase);
        if (mbidSet.Count == 0)
        {
            return candidates;
        }

        // OQ-5: paged bulk with streaming filter — only playlist candidates
        // are retained, so memory is bounded by page size + candidate count.
        var startIndex = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = _facade.GetRecordingTaggedAudioItems(startIndex, PageSize);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var item in page)
            {
                if (item.ProviderIds.TryGetValue(RecordingProviderId, out var recordingMbid)
                    && mbidSet.Contains(recordingMbid))
                {
                    if (!candidates.TryGetValue(recordingMbid, out var pool))
                    {
                        pool = [];
                        candidates[recordingMbid] = pool;
                    }

                    pool.Add(item);
                }
            }

            startIndex += page.Count;
            if (page.Count < PageSize)
            {
                break;
            }
        }

        progress?.Report(0.45);
        return candidates;
    }

    private static (Audio? Item, MatchStrategy Strategy, bool Ambiguous) MatchByName(
        LbTrack track,
        IReadOnlyList<Audio> found)
    {
        if (found.Count == 0)
        {
            return (null, MatchStrategy.None, false);
        }

        var trackTitle = NameNormalizer.Normalize(track.Title);
        var trackTitleStripped = NameNormalizer.NormalizeWithoutBrackets(track.Title);
        var trackArtist = NameNormalizer.Normalize(track.Artist);
        var trackArtistStripped = NameNormalizer.NormalizeWithoutBrackets(track.Artist);

        // T1: exact normalized (artist, title).
        var pool = found
            .Where(i => HasArtist(i, trackArtist) && NameNormalizer.Normalize(i.Name) == trackTitle)
            .ToList();
        var strategy = MatchStrategy.NameExact;

        // T2: bracket-stripped on both sides.
        if (pool.Count == 0)
        {
            pool = found
                .Where(i => HasArtist(i, trackArtistStripped)
                            && NameNormalizer.NormalizeWithoutBrackets(i.Name) == trackTitleStripped)
                .ToList();
            strategy = MatchStrategy.NameLoose;
        }

        // T3: bracket-stripped title + artist containment ("The Beatles" vs "Beatles").
        if (pool.Count == 0 && trackTitleStripped.Length != 0 && trackArtistStripped.Length != 0)
        {
            pool = found
                .Where(i => NameNormalizer.NormalizeWithoutBrackets(i.Name) == trackTitleStripped
                            && ArtistContains(i, trackArtistStripped))
                .ToList();
            strategy = MatchStrategy.NameLoose;
        }

        if (pool.Count == 0)
        {
            return (null, MatchStrategy.None, false);
        }

        // FR-13 (should): artist-MBID tiebreak among name candidates.
        if (track.ArtistMbids.Count != 0)
        {
            var mbidHits = pool
                .Where(i => i.ProviderIds.TryGetValue(ArtistProviderId, out var artistMbid)
                            && track.ArtistMbids.Contains(artistMbid, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (mbidHits.Count > 0)
            {
                pool = mbidHits;
            }
        }

        var ambiguous = pool.Count > 1;
        return (PickByAmbiguityPolicy(pool, track), strategy, ambiguous);
    }

    private static bool HasArtist(Audio item, string normalizedArtist)
        => normalizedArtist.Length != 0
           && ItemArtists(item).Any(a => NameNormalizer.Normalize(a) == normalizedArtist);

    private static bool ArtistContains(Audio item, string normalizedArtistStripped)
        => ItemArtists(item)
            .Select(NameNormalizer.NormalizeWithoutBrackets)
            .Any(a => a.Length != 0
                      && (a.Contains(normalizedArtistStripped, StringComparison.Ordinal)
                          || normalizedArtistStripped.Contains(a, StringComparison.Ordinal)));

    private static IEnumerable<string> ItemArtists(Audio item)
        => (item.Artists ?? []).Concat(item.AlbumArtists ?? []);

    /// <summary>
    /// OQ-6 ambiguity policy: exact normalized-title match first, then lowest
    /// Jellyfin item Id. Deterministic; callers count and log ambiguities.
    /// </summary>
    private static Audio PickByAmbiguityPolicy(IReadOnlyList<Audio> pool, LbTrack track)
    {
        if (pool.Count == 1)
        {
            return pool[0];
        }

        var trackTitle = NameNormalizer.Normalize(track.Title);
        var exact = pool.Where(i => NameNormalizer.Normalize(i.Name) == trackTitle).ToList();
        return (exact.Count > 0 ? exact : pool).OrderBy(i => i.Id).First();
    }
}
