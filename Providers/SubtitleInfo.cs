using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Text;

namespace subbuzz.Providers
{
    public class SubtitleInfo : RemoteSubtitleInfo
    {
#if !EMBY
        public bool? IsForced { get; set; }
#endif
        public float Score { get; set; }
        public string SubBuzzProviderName { get; set; }

        public SubtitleInfo()
        {
            IsForced = false;
            Score = 0;
        }

    }
}
