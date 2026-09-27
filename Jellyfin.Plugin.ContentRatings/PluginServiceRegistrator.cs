using Jellyfin.Plugin.ContentRatings.Services;
using Jellyfin.Plugin.ContentRatings.Sources;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ContentRatings;

/// <summary>
/// Registers the plugin's services with the server.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IRatingSource, TmdbRatingSource>();
        serviceCollection.AddSingleton<IRatingSource, TvdbRatingSource>();
        serviceCollection.AddSingleton<RunStateStore>();
        serviceCollection.AddSingleton<ContentRatingUpdater>();
    }
}
