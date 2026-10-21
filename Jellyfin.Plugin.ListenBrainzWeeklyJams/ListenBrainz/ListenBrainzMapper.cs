using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;

/// <summary>
/// Maps verified JSPF wire shapes to domain records. Shapes verified against
/// the live API on 2026-10-20 (docs/requirements.md §2.1): responses have no
/// <c>payload</c> wrapper.
/// </summary>
internal static class ListenBrainzMapper
{
    /// <summary>The JSPF playlist extension key.</summary>
    public const string PlaylistExtensionKey = "https://musicbrainz.org/doc/jspf#playlist";

    /// <summary>The JSPF track extension key.</summary>
    public const string TrackExtensionKey = "https://musicbrainz.org/doc/jspf#track";

    /// <summary>
    /// Maps a JSPF playlist to a summary; null when the identifier is not a
    /// parseable playlist MBID (defensive — malformed entries are skipped).
    /// </summary>
    /// <param name="playlist">The JSPF playlist.</param>
    /// <returns>The summary, or null.</returns>
    public static LbPlaylistSummary? ToSummary(JspfPlaylist playlist)
    {
        if (!TryParseMbid(playlist.Identifier, out var mbid))
        {
            return null;
        }

        var createdAt = DateTimeOffset.TryParse(playlist.Date, out var date) ? date : default;
        var sourcePatch = playlist.Extension?
            .GetValueOrDefault(PlaylistExtensionKey)?
            .AdditionalMetadata?
            .AlgorithmMetadata?
            .SourcePatch;

        return new LbPlaylistSummary(mbid, playlist.Title ?? string.Empty, createdAt, sourcePatch);
    }

    /// <summary>
    /// Maps a JSPF playlist to full detail with ordered tracks.
    /// </summary>
    /// <param name="playlist">The JSPF playlist.</param>
    /// <returns>The playlist detail.</returns>
    public static LbPlaylistDetail ToDetail(JspfPlaylist playlist)
    {
        var mbid = TryParseMbid(playlist.Identifier, out var parsed) ? parsed : Guid.Empty;
        var createdAt = DateTimeOffset.TryParse(playlist.Date, out var date) ? date : default;

        var tracks = (playlist.Track ?? [])
            .Select((track, index) => ToTrack(track, index + 1))
            .ToList();

        return new LbPlaylistDetail(mbid, playlist.Title ?? string.Empty, createdAt, tracks);
    }

    private static LbTrack ToTrack(JspfTrack track, int position)
    {
        var recordingMbid = (track.Identifier ?? [])
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(MbidFromUrl)
            .FirstOrDefault() ?? string.Empty;

        var artistMbids = (track.Extension?
            .GetValueOrDefault(TrackExtensionKey)?
            .AdditionalMetadata?
            .Artists ?? [])
            .Where(a => !string.IsNullOrEmpty(a.ArtistMbid))
            .Select(a => a.ArtistMbid!)
            .ToList();

        return new LbTrack(
            position,
            recordingMbid,
            track.Title ?? string.Empty,
            track.Creator ?? string.Empty,
            track.Album ?? string.Empty,
            artistMbids);
    }

    private static string MbidFromUrl(string url)
        => string.IsNullOrEmpty(url) ? string.Empty : url.TrimEnd('/').Split('/')[^1].ToLowerInvariant();

    private static bool TryParseMbid(string? identifierUrl, out Guid mbid)
    {
        var tail = string.IsNullOrEmpty(identifierUrl) ? null : identifierUrl.TrimEnd('/').Split('/')[^1];
        return Guid.TryParse(tail, out mbid);
    }
}
