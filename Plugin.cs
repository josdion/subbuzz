using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using subbuzz.Configuration;
using subbuzz.Extensions;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.IO;

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
        private readonly IApplicationPaths _appPaths;

        private readonly object _cacheSyncLock = new object();
        private FileCache? _cache = null;

        public override string Name => NAME;
        public override string Description => "Download subtitles from various sites";
        public override Guid Id => Guid.Parse("5aeab01b-2ef8-45c6-bb6b-16ce9cb268d4");
        public static Plugin? Instance { get; private set; } = null;
        public FileCache? Cache
        {
            get {
                return GetCache();
            }
        }

        public Plugin(
			IApplicationPaths applicationPaths, 
			IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _appPaths = applicationPaths;
        }

        private FileCache? GetCache()
        {
            lock (_cacheSyncLock)
            {
                if (_cache != null && _cache.CacheDir == base.Configuration.Cache.BasePath)
                    return _cache;

                if (base.Configuration.Cache.BasePath.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        Directory.CreateDirectory(base.Configuration.Cache.BasePath);
                        _cache = new FileCache(base.Configuration.Cache.BasePath);
                        return _cache;
                    }
                    catch
                    {
                    }
                }

                try
                {
                    string defaultPath = Path.Combine(_appPaths.CachePath, NAME);
                    _cache = new FileCache(defaultPath);
                    base.Configuration.Cache.BasePath = _cache.CacheDir;
                    base.SaveConfiguration();
                    return _cache;
                }
                catch
                {
                    return null;
                }
            }
        }

        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            base.UpdateConfiguration(configuration);
        }

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
                    MenuSection = "server",
                    MenuIcon = "subtitles",
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
