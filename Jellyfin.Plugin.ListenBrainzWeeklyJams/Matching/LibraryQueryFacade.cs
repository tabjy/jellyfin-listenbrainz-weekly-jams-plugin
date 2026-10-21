using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;

/// <summary>
/// The production query facade over <see cref="ILibraryManager"/>. Query shapes
/// verified against Jellyfin v10.11.9: <c>HasAnyProviderId</c> with an empty
/// value selects items that have the provider set (server-side, indexed);
/// <c>NameContains</c> is a substring title search.
/// </summary>
public sealed class LibraryQueryFacade : ILibraryQueryFacade
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryQueryFacade"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public LibraryQueryFacade(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public IReadOnlyList<Audio> GetRecordingTaggedAudioItems(int startIndex, int limit)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Audio },
            // null value (checked with IsNullOrEmpty server-side) means "any
            // item having this provider set" — the includeAny branch.
            HasAnyProviderId = new Dictionary<string, string> { { MetadataProvider.MusicBrainzRecording.ToString(), null! } },
            StartIndex = startIndex,
            Limit = limit
        };

        return _libraryManager.GetItemList(query).OfType<Audio>().ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<Audio> SearchAudioByTitle(string title, int limit)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Audio },
            NameContains = title,
            Limit = limit
        };

        return _libraryManager.GetItemList(query).OfType<Audio>().ToList();
    }
}
