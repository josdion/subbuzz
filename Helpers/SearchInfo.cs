using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using System;

namespace subbuzz.Helpers
{

    class SearchInfo
    {
        public string SearchText = "";
        public float? VideoFps = null;
        public string ImdbId = "";
        public string Lang = ""; // two letter lower case language code
        public CultureDto LanguageInfo;

        public static SearchInfo GetSearchInfo(
            SubtitleSearchRequest request,
            ILocalizationManager localize,
            ILibraryManager lib,
            string episode_format = "")
        {
            var res = new SearchInfo();

#if EMBY
            res.LanguageInfo = localize.FindLanguageInfo(request.Language.AsSpan());
#else
            res.LanguageInfo = localize.FindLanguageInfo(request.Language);
#endif
            res.Lang = res.LanguageInfo.TwoLetterISOLanguageName.ToLower();

            BaseItem libItem = lib.FindByPath(request.MediaPath, false);
            if (libItem == null)
            {
                return res;
            }

            if (request.ContentType == VideoContentType.Movie)
            {
                Movie mv = libItem as Movie;
                MediaStream media = mv.GetDefaultVideoStream();
                if (media != null) res.VideoFps = media.AverageFrameRate;

                res.SearchText = !String.IsNullOrEmpty(mv.OriginalTitle) ? mv.OriginalTitle : mv.Name;
            }
            else
            if (request.ContentType == VideoContentType.Episode && !String.IsNullOrEmpty(episode_format))
            {
                Episode ep = libItem as Episode;
                MediaStream media = ep.GetDefaultVideoStream();
                if (media != null) res.VideoFps = media.AverageFrameRate;

                // episode format {0} - series name, {1} - season, {2} - episode
                res.SearchText = String.Format(episode_format,
                    !String.IsNullOrEmpty(ep.Series.OriginalTitle) ? ep.Series.OriginalTitle : ep.Series.Name,
                    request.ParentIndexNumber ?? 0, 
                    request.IndexNumber ?? 0);
            }

            res.SearchText = res.SearchText.Replace(':', ' ').Replace("  ", " ");

            //res.ImdbId = request.GetProviderId(MetadataProviders.Imdb);

            return res;
        }
    }
}
