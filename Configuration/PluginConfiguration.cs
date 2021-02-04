using MediaBrowser.Model.Plugins;

namespace subbuzz
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool EnableSubssabbz { get; set; }
        public bool EnableSubsunacsNet { get; set; }
        public bool EnableYavkaNet { get; set; }
        public bool EncodeSubtitlesToUTF8 { get; set; }

        public PluginConfiguration()
        {
            EnableSubssabbz = true;
            EnableSubsunacsNet = true;
            EnableYavkaNet = true;
            EncodeSubtitlesToUTF8 = false;
        }
    }
}
