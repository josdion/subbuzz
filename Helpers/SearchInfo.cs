using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
            string episode_format = "",
            Dictionary<string, string> inconsistentTvs = null,
            Dictionary<string, string> inconsistentMovies = null
            )
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
                if (inconsistentMovies != null)
                {
                    res.SearchText = inconsistentMovies.Aggregate(res.SearchText, (current, value) =>
                        Regex.Replace(current, Regex.Escape(value.Key), value.Value, RegexOptions.IgnoreCase));
                }

                mv.ProviderIds.TryGetValue("Imdb", out res.ImdbId);
            }
            else
            if (request.ContentType == VideoContentType.Episode && !String.IsNullOrEmpty(episode_format))
            {
                Episode ep = libItem as Episode;
                MediaStream media = ep.GetDefaultVideoStream();
                if (media != null) res.VideoFps = media.AverageFrameRate;

                string title = !String.IsNullOrEmpty(ep.Series.OriginalTitle) ? ep.Series.OriginalTitle : ep.Series.Name;
                if (inconsistentTvs != null)
                {
                    title = inconsistentTvs.Aggregate(title, (current, value) =>
                        Regex.Replace(current, Regex.Escape(value.Key), value.Value, RegexOptions.IgnoreCase));
                }

                // episode format {0} - series name, {1} - season, {2} - episode
                res.SearchText = String.Format(episode_format,
                    title,
                    request.ParentIndexNumber ?? 0, 
                    request.IndexNumber ?? 0);

                ep.Series.ProviderIds.TryGetValue("Imdb", out res.ImdbId);
                //ep.ProviderIds.TryGetValue("Imdb", out res.ImdbIdEpisode);
            }

            res.SearchText = res.SearchText.Replace(':', ' ').Replace("  ", " ");

            return res;
        }
    }
}
