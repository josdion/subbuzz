using MediaBrowser.Model.Plugins;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;

namespace subbuzz.Configuration
{
    public class SubPostProcessingCfg
    {
        public bool EncodeSubtitlesToUTF8 { get; set; } = true;
        public bool AdjustDuration { get; set; } = false;
        public double AdjustDurationCps { get; set; } = 15.0;
        public bool AdjustDurationExtendOnly { get; set; } = true;
    }

    public class SubEncodingCfg
    {
        public string DefaultEncoding { get; set; } = Encoding.Default.WebName;
        public bool AutoDetectEncoding { get; set; } = true;

        [XmlIgnoreAttribute]
        public List<string> Encodings { get;  }

        public SubEncodingCfg()
        {
            Encodings = new List<string>();
            foreach (var e in Encoding.GetEncodings()) Encodings.Add(e.Name);
            Encodings.Sort();
        }

        public Encoding Get()
        {
            try
            {
                return Encoding.GetEncoding(DefaultEncoding);
            }
            catch
            {
                return Encoding.Default;
            }
        }

        public SubEncodingCfg GetUtf8()
        {
            return new SubEncodingCfg
            {
                // override default encoding selected by user for some providers
                // like opensubtitle.com as all their subtitles are UTF encoded
                DefaultEncoding = Encoding.UTF8.WebName,
                AutoDetectEncoding = AutoDetectEncoding,
            };
        }

    }

    public class CacheCfg
    {
        public bool Subtitle { get; set; } = true;
        public int SubLifeInMinutes { get; set; } = 24 * 60;
        public bool Search { get; set; } = true;
        public int SearchLifeInMinutes { get; set; } = 4 * 60;
        public string BasePath { get; set; } = string.Empty;

        public int GetSubLife() => Subtitle ? SubLifeInMinutes : -1;
        public int GetSearchLife() => Search ? SearchLifeInMinutes : -1;
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool EnableAddic7ed { get; set; } = true;
        public bool EnableOpenSubtitles { get; set; } = true;
        public bool EnablePodnapisiNet { get; set; } = true;
        public bool EnableSubf2m { get; set; } = true;
        public bool EnableSubscene { get; set; } = true;
        public bool EnableSubssabbz { get; set; } = true;
        public bool EnableSubsunacsNet { get; set; } = true;
        public bool EnableYavkaNet { get; set; } = true;
        public bool EnableYifySubtitles { get; set; } = true;
        public float HashMatchByScore { get; set; } = 100;
        public float MinScore { get; set; } = 50;

        public string OpenSubUserName { get; set; } = string.Empty;
        public string OpenSubPassword { get; set; } = string.Empty;
        public string OpenSubApiKey { get; set; } = string.Empty;
        public string OpenSubToken { get; set; } = string.Empty;
        public bool OpenSubUseHash { get; set; } = true;

        public SubEncodingCfg SubEncoding { get; set; }
        public SubPostProcessingCfg SubPostProcessing { get; set; }
        public CacheCfg Cache { get; set; }

        public PluginConfiguration()
        {
            SubEncoding = new SubEncodingCfg();
            SubPostProcessing = new SubPostProcessingCfg();
            Cache = new CacheCfg();
        }

#if NO_HTML
        public bool SubtitleInfoWithHtml { get; set; } = false;
#else
        public bool SubtitleInfoWithHtml { get; set; } = true;
#endif
    }
}
