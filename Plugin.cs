using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using System.IO;
using MediaBrowser.Model.Drawing;

namespace subbuzz
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasThumbImage
    {
        public const string NAME = "subbuzz";

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override Guid Id => new Guid("{5AEAB01B-2EF8-45C6-BB6B-16CE9CB268D4}");
        public override string Name => NAME;
        public override string Description => "Download subtitles from various sites";
        public static Plugin Instance { get; private set; }

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

    }
}
