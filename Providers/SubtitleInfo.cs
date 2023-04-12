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
        public string PageLink { get; set; } = string.Empty;

        /// <summary>
        /// Subtitles for the deaf and hard of hearing (SDH) 
        /// </summary>
        public bool? IsSdh { get; set; } = null;
        public float Score { get; set; }
        public string SubBuzzProviderName { get; set; }

        public SubtitleInfo()
        {
            IsForced = false;
            Score = 0;
        }

    }
}
