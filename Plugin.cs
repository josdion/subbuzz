using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using subbuzz.Helpers;
using subbuzz.Extensions;

#if EMBY
using MediaBrowser.Model.Drawing;
#endif

namespace subbuzz
{
#if EMBY
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage
    {
        public const string SERVER = "Emby";
#else
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public const string SERVER = "Jellyfin";
#endif
        public const string NAME = "subbuzz";
        public FileCache Cache = null;
        private readonly IApplicationPaths _appPaths;

        public Plugin(
			IApplicationPaths applicationPaths, 
			IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            Cache = null;
            _appPaths = applicationPaths;
        }

        public void InitCache()
        {
            if (Cache != null && Cache.CacheDir == Configuration.Cache.BasePath)
                return;

            if (Configuration.Cache.BasePath.IsNotNullOrWhiteSpace())
            {
                try
                {
                    Directory.CreateDirectory(Configuration.Cache.BasePath);
                    Cache = new FileCache(Configuration.Cache.BasePath);
                    return;
                }
                catch
                {
                }
            }

            try
            {
                string defaultPath = Path.Combine(_appPaths.CachePath, NAME);
                Cache = new FileCache(defaultPath);
                Configuration.Cache.BasePath = Cache.CacheDir;
            }
            catch 
            {
            }
        }

        public override void SaveConfiguration()
        {
            InitCache();
            base.SaveConfiguration();
        }

        public override string Name => NAME;
        public override string Description => "Download subtitles from various sites";
        public override Guid Id => Guid.Parse("5aeab01b-2ef8-45c6-bb6b-16ce9cb268d4");
        public static Plugin Instance { get; private set; }

#if EMBY
        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.png");
        }

        public ImageFormat ThumbImageFormat
        {
            get
            {
                return ImageFormat.Png;
            }
        }
#endif

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    DisplayName = "SubBuzz",
                    Name = "SubbuzzConfigPage",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                    EnableInMainMenu = true,
                    MenuIcon = "closed_caption"
                },
                new PluginPageInfo
                {
                    Name = "SubbuzzConfigPageJs",
                    EmbeddedResourcePath = GetType().Namespace + $".Configuration.{SERVER}.configPage.js"
                },

                new PluginPageInfo
                {
                    Name = "SubbuzzConfigCachePage",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configCachePage.html",
                    MenuIcon = "closed_caption"
                },
                new PluginPageInfo
                {
                    Name = "SubbuzzConfigCachePageJs",
                    EmbeddedResourcePath = GetType().Namespace + $".Configuration.{SERVER}.configCachePage.js"
                },

            };
        }

    }
}
