using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using subbuzz.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace subbuzz.Helpers
{

    public class SearchInfo
    {
        public string SearchText = "";
        public string SearchEpByName = "";
        public string SearchSeason = "";
        public float? VideoFps = null;
        public string ImdbId = "";
        public int    ImdbIdInt = 0;
        public string ImdbIdEpisode = "";
        public int    ImdbIdEpisodeInt = 0;
        public string Lang = ""; // two letter lower case language code
        public CultureDto LanguageInfo;
        public VideoContentType VideoType;
        public int SeasonNumber = 0;
        public int EpisodeNumber = 0;

        public static SearchInfo GetSearchInfo(
            SubtitleSearchRequest request,
            ILocalizationManager localize,
            ILibraryManager lib,
            string episode_format = "",
            string season_format = "",
            Dictionary<string, string> inconsistentTvs = null,
            Dictionary<string, string> inconsistentMovies = null
            )
        {
            var res = new SearchInfo();
            res.VideoType = request.ContentType;

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

            if (res.VideoType == VideoContentType.Movie)
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
            if (res.VideoType == VideoContentType.Episode && !String.IsNullOrEmpty(episode_format))
            {
                res.SeasonNumber = request.ParentIndexNumber ?? 0;
                res.EpisodeNumber = request.IndexNumber ?? 0;

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
                    res.SeasonNumber,
                    res.EpisodeNumber);

                res.SearchSeason = String.Format(season_format,
                    title,
                    res.SeasonNumber);

                string titleEp = !String.IsNullOrEmpty(ep.OriginalTitle) ? ep.OriginalTitle : ep.Name;
                if (titleEp.ContainsIgnoreCase(title)) res.SearchEpByName = titleEp;
                else res.SearchEpByName = String.Format("{0} {1}", title, titleEp);

                ep.Series.ProviderIds.TryGetValue("Imdb", out res.ImdbId);
                ep.ProviderIds.TryGetValue("Imdb", out res.ImdbIdEpisode);
            }

            res.SearchText = res.SearchText.Replace(':', ' ').Replace("  ", " ");
            res.SearchEpByName = res.SearchEpByName.Replace(':', ' ').Replace("  ", " ");

            var regexImdbId = new Regex(@"tt(\d+)");

            if (!String.IsNullOrWhiteSpace(res.ImdbId))
            {
                var match = regexImdbId.Match(res.ImdbId);
                if (match.Success && match.Groups.Count > 1)
                    res.ImdbIdInt = int.Parse(match.Groups[1].ToString());
            }

            if (!String.IsNullOrWhiteSpace(res.ImdbIdEpisode))
            {
                var match = regexImdbId.Match(res.ImdbIdEpisode);
                if (match.Success && match.Groups.Count > 1)
                    res.ImdbIdEpisodeInt = int.Parse(match.Groups[1].ToString());
            }

            return res;
        }

        public bool CheckImdbId(int imdb)
        {
            if (imdb > 0)
            {
                if (imdb != ImdbIdInt)
                {
                    if (VideoType == VideoContentType.Episode)
                    {
                        if (ImdbIdEpisodeInt > 0 && imdb != ImdbIdEpisodeInt)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true; // if IMDB ID match or no info
        }
    }
}
