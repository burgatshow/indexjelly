using Jellyfin.Plugin.IndexJelly.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.IndexJelly;

/// <summary>Registers the activity tracker, which has to run from server start to see the refresh events.</summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ActivityTracker>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<ActivityTracker>());
    }
}
