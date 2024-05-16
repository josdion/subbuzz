using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;
using subbuzz.Providers;

namespace subbuzz;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHos)
    {
        serviceCollection.AddSingleton<ISubtitleProvider, SubBuzz>();
    }
}
