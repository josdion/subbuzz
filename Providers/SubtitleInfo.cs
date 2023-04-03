using MediaBrowser.Model.Providers;

namespace subbuzz.Providers
{
    public class SubtitleInfo : RemoteSubtitleInfo
    {
#if !EMBY
        public bool? IsForced { get; set; }
#endif

#if EMBY
        public new string ThreeLetterISOLanguageName
        {
            get
            {
                return base.Language;
            }
            set
            {
                base.Language = value;
            }
        }
#endif 

        // Subtitles for the deaf and hard of hearing (SDH) 
        public bool? Sdh { get; set; } = null;
        public float Score { get; set; }
        public string SubBuzzProviderName { get; set; }

        public SubtitleInfo()
        {
            IsForced = false;
            Score = 0;
        }

    }
}
