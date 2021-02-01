using MediaBrowser.Model.Plugins;

namespace subbuzz
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool EncodeSubtitlesToUTF8 { get; set; }

        public PluginConfiguration()
        {
            EncodeSubtitlesToUTF8 = false;
        }
    }
}
