using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities.Audio;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;

/// <summary>
/// Thin query abstraction over ILibraryManager, so the matcher is unit-testable
/// without a server (design D-16).
/// </summary>
public interface ILibraryQueryFacade
{
    /// <summary>
    /// Gets a page of audio items that carry a MusicBrainzRecording provider ID
    /// (any value) — the bulk leg of the OQ-5 strategy.
    /// </summary>
    /// <param name="startIndex">The page start index.</param>
    /// <param name="limit">The page size.</param>
    /// <returns>The page of items.</returns>
    IReadOnlyList<Audio> GetRecordingTaggedAudioItems(int startIndex, int limit);

    /// <summary>
    /// Searches audio items whose name contains the given title — the per-miss
    /// name-fallback leg.
    /// </summary>
    /// <param name="title">The title to search for.</param>
    /// <param name="limit">The maximum number of candidates.</param>
    /// <returns>The candidate items.</returns>
    IReadOnlyList<Audio> SearchAudioByTitle(string title, int limit);
}
