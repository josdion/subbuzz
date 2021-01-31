using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using System;

#if EMBY
using System.IO;
using MediaBrowser.Model.Drawing;
using subbuzz.Logging;
using ILogger = MediaBrowser.Model.Logging.ILogger;
#else
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger<subbuzz.Plugin>;
#endif

namespace subbuzz
{
#if EMBY
    public class Plugin : BasePlugin<PluginConfiguration>, IHasThumbImage
#else
    public class Plugin : BasePlugin<PluginConfiguration>
#endif
    {
        public const string NAME = "subbuzz";

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger logger)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            logger.LogInformation("subbuzz is starting.");
        }

        public override Guid Id => new Guid("{5AEAB01B-2EF8-45C6-BB6B-16CE9CB268D4}");
        public override string Name => NAME;
        public override string Description => "Download subtitles from various sites";
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

    }
}
