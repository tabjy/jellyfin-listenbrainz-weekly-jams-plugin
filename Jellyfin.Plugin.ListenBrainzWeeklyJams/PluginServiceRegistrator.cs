using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams;

/// <summary>
/// Registers plugin services with the server's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ListenBrainzApiClient>();
    }
}
