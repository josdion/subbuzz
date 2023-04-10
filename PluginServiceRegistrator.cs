using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;
using subbuzz.Providers;

namespace subbuzz;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<ISubtitleProvider, SubBuzz>();
    }
}
