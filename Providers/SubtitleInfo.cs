using MediaBrowser.Model.Providers;

namespace subbuzz.Providers
{
    public class SubtitleInfo : RemoteSubtitleInfo
    {
#if JELLYFIN
        public bool? IsForced { get { return Forced; } set { Forced = value; } }
        public bool? IsHearingImpaired { get { return HearingImpaired; } set { HearingImpaired = value; } }
#endif

#if JELLYFIN_108
        public bool? Forced { get; set; }
        public bool? HearingImpaired { get; set; }
        public float? FrameRate { get; set; }
        public bool? AiTranslated { get; set; }
        public bool? MachineTranslated { get; set; }
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

        public float? FrameRate { get; set; }
        public bool? AiTranslated { get; set; }
        public bool? MachineTranslated { get; set; }

#endif

#if EMBY_47
        public bool? IsHearingImpaired { get; set; } = null;
#endif

        public string PageLink { get; set; } = string.Empty;

        public float Score { get; set; }

        public string SubBuzzProviderName { get; set; }

        public SubtitleInfo()
        {
            IsForced = false;
            Score = 0;
        }

    }
}
