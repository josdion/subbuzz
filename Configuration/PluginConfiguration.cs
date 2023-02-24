using MediaBrowser.Model.Plugins;

namespace subbuzz
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool EnableOpenSubtitles { get; set; }
        public bool EnablePodnapisiNet { get; set; }
        public bool EnableSubf2m { get; set; }
        public bool EnableSubscene { get; set; }
        public bool EnableSubssabbz { get; set; }
        public bool EnableSubsunacsNet { get; set; }
        public bool EnableYavkaNet { get; set; }
        public bool EnableYifySubtitles { get; set; }
        public bool EncodeSubtitlesToUTF8 { get; set; }
        public float HashMatchByScore { get; set; }
        public float MinScore { get; set; }

        public string OpenSubUserName { get; set; }
        public string OpenSubPassword { get; set; }
        public string OpenSubApiKey { get; set; }
        public string OpenSubToken { get; set; }
        public bool OpenSubUseHash { get; set; }

        public PluginConfiguration()
        {
            EnableOpenSubtitles = true;
            EnablePodnapisiNet = true;
            EnableSubf2m = true;
            EnableSubscene = true;
            EnableSubssabbz = true;
            EnableSubsunacsNet = true;
            EnableYavkaNet = true;
            EnableYifySubtitles = true;
            EncodeSubtitlesToUTF8 = false;
            HashMatchByScore = 100;
            MinScore = 50;

            OpenSubUserName = string.Empty;
            OpenSubPassword = string.Empty;
            OpenSubApiKey = string.Empty;
            OpenSubToken = string.Empty;
            OpenSubUseHash = true;
        }
    }
}
