using System;
using System.IO;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Playlists;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams;

/// <summary>
/// Registers plugin services with the server's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IListenBrainzApiClient, ListenBrainzApiClient>();
        serviceCollection.AddSingleton<ILibraryQueryFacade, LibraryQueryFacade>();
        serviceCollection.AddSingleton<LibraryMatcher>();
        serviceCollection.AddSingleton<IPlaylistLookup, PlaylistLookup>();
        serviceCollection.AddSingleton<IPlaylistWriter, PlaylistWriter>();
        serviceCollection.AddSingleton<SyncOrchestrator>();
        serviceCollection.AddSingleton(sp => new SyncStateStore(
            Path.Combine(
                (Plugin.Instance ?? throw new InvalidOperationException("Plugin instance not available")).DataFolderPath,
                "state.json"),
            sp.GetRequiredService<ILogger<SyncStateStore>>()));
    }
}
