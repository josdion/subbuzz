using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;

#if EMBY
using System.IO;
using MediaBrowser.Model.Drawing;
#endif

namespace subbuzz
{
#if EMBY
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage
#else
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
#endif
    {
        public const string NAME = "subbuzz";

        public Plugin(
			IApplicationPaths applicationPaths, 
			IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
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
                    Name = "SubbuzzConfigPage",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                }
            };
        }

    }
}
