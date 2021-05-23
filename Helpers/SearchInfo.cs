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
using System.Globalization;
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
        public int ImdbIdInt = 0;
        public string ImdbIdEpisode = "";
        public int ImdbIdEpisodeInt = 0;
        public string Lang = ""; // two letter lower case language code
        public CultureDto LanguageInfo;
        public VideoContentType VideoType;
        public int? SeasonNumber = null;
        public int? EpisodeNumber = null;
        public int? Year = null;
        public string TitleMovie = "";
        public string TitleSeries = "";
        public Parser.EpisodeInfo EpInfo = null;
        public Parser.MovieInfo MvInfo = null;

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

                res.TitleMovie = res.SearchText;
                res.MvInfo = Parser.Movie.ParsePath(mv.Path);
                if (res.MvInfo != null && res.MvInfo.Year == 0)
                    res.MvInfo.Year = request.ProductionYear ?? 0;
            }
            else
            if (res.VideoType == VideoContentType.Episode && !String.IsNullOrEmpty(episode_format))
            {
                res.SeasonNumber = request.ParentIndexNumber;
                res.EpisodeNumber = request.IndexNumber;

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
                if (res.SeasonNumber != null && res.EpisodeNumber != null)
                    res.SearchText = String.Format(episode_format,
                        title,
                        res.SeasonNumber,
                        res.EpisodeNumber);

                // season format {0} - series name, {1} - season
                if (res.SeasonNumber != null)
                    res.SearchSeason = String.Format(season_format,
                        title,
                        res.SeasonNumber);

                string titleEp = !String.IsNullOrEmpty(ep.OriginalTitle) ? ep.OriginalTitle : ep.Name;
                if (titleEp.ContainsIgnoreCase(title)) res.SearchEpByName = titleEp;
                else res.SearchEpByName = String.Format("{0} {1}", title, titleEp);

                ep.Series.ProviderIds.TryGetValue("Imdb", out res.ImdbId);
                ep.ProviderIds.TryGetValue("Imdb", out res.ImdbIdEpisode);

                res.TitleSeries = title;
                res.TitleMovie = res.SearchEpByName;
                res.EpInfo = Parser.Episode.ParsePath(ep.Path);
                if (res.EpInfo != null && res.EpInfo.SeriesTitleInfo.Year == 0)
                    res.EpInfo.SeriesTitleInfo.Year = request.ProductionYear ?? 0;
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

            res.Year = request.ProductionYear;

            return res;
        }

        public bool CheckImdbId(int imdb, ref SubtitleScore score)
        {
            if (imdb > 0)
            {
                if (VideoType == VideoContentType.Episode)
                {
                    if (ImdbIdEpisodeInt > 0 && imdb == ImdbIdEpisodeInt)
                    {
                        score.AddMatch("imdb_episode");
                        return true;
                    }
                }

                if (ImdbIdInt > 0)
                {
                    if (imdb == ImdbIdInt)
                    {
                        score.AddMatch("imdb");
                        return true;
                    }

                    if (VideoType == VideoContentType.Episode && (SeasonNumber != null && SeasonNumber == 0))
                    {
                        return true; // ignore IMDB mismatch for special episodes
                    }

                    return false; // IMDB doesn't match
                }
            }

            return true; // no IMDB info
        }

        public bool CheckFps(string fpsText, ref SubtitleScore score)
        {
            float fps = 0;
            try
            {
                fps = float.Parse(fpsText, CultureInfo.InvariantCulture);
            }
            catch
            {
            }

            if (VideoFps != null && VideoFps > 0 && fps > 0)
            {
                if (Math.Abs((float)(VideoFps - fps)) < 0.1)
                {
                    score.AddMatch("frame_rate");
                    return true;
                }

                return false; // frame rate doesn't match
            }

            return true; // no frame rate information
        }

        public bool CheckEpisode(Parser.EpisodeInfo epInfo, ref SubtitleScore score)
        {
            if (epInfo == null)
            {
                return true; // no enough info to decide if the episode should be skipped
            }

            bool season = true;
            bool episode = true;

            string title = Parser.Episode.NormalizeTitle(epInfo.SeriesTitleInfo.TitleWithoutYear);
            if (title == Parser.Episode.NormalizeTitle(TitleSeries))
            {
                score.AddMatch("title");
            }

            if (Year != null && Year == epInfo.SeriesTitleInfo.Year)
            {
                score.AddMatch("year");
            }

            if (SeasonNumber != null)
            {
                if (SeasonNumber == epInfo.SeasonNumber)
                {
                    score.AddMatch("season");
                }
                else
                if (SeasonNumber > 0)
                    season = false;
            }

            if (EpisodeNumber != null && epInfo.EpisodeNumbers.Length > 0)
            {
                if (epInfo.EpisodeNumbers.Contains(EpisodeNumber ?? 0))
                {
                    score.AddMatch("episode");
                }
                else
                if (EpisodeNumber > 0 && epInfo.EpisodeNumbers[0] > 0)
                    episode = false;
            }

            if (EpInfo != null)
            {
                if (epInfo.ReleaseGroup.IsNotNullOrWhiteSpace() && 
                    EpInfo.ReleaseGroup.IsNotNullOrWhiteSpace() &&
                    epInfo.ReleaseGroup.EqualsIgnoreCase(EpInfo.ReleaseGroup))
                    score.AddMatch("release_group");

                MatchQuality(EpInfo.Quality, epInfo.Quality, ref score);
            }

            return season && episode;
        }

        public bool CheckMovie(Parser.MovieInfo mvInfo, ref SubtitleScore score, bool addEmptyMatches = false)
        {
            if (mvInfo == null)
            {
                return true; // no enough info to decide if the movie should be skipped
            }

            string title = Parser.Movie.NormalizeTitle(mvInfo.MovieTitle);
            if (title == Parser.Movie.NormalizeTitle(TitleMovie))
            {
                score.AddMatch("title");
            }

            if (Year != null && Year == mvInfo.Year)
            {
                score.AddMatch("year");
            }

            if (MvInfo != null)
            {
                if (mvInfo.ReleaseGroup.IsNotNullOrWhiteSpace() &&
                    MvInfo.ReleaseGroup.IsNotNullOrWhiteSpace() &&
                    mvInfo.ReleaseGroup.EqualsIgnoreCase(MvInfo.ReleaseGroup))
                {
                    score.AddMatch("release_group");
                }

                if (mvInfo.Edition.IsNullOrWhiteSpace() && 
                    MvInfo.Edition.IsNullOrWhiteSpace())
                {
                    if (addEmptyMatches)
                        score.AddMatch("edition");
                }
                else
                if (mvInfo.Edition.IsNotNullOrWhiteSpace() &&
                    MvInfo.Edition.IsNotNullOrWhiteSpace() &&
                    mvInfo.Edition.Equals(MvInfo.Edition))
                {
                    score.AddMatch("edition");
                }

                MatchQuality(MvInfo.Quality, mvInfo.Quality, ref score, addEmptyMatches);
            }

            return true;
        }

        protected void MatchQuality(Parser.Qualities.QualityModel qm1, Parser.Qualities.QualityModel qm2, 
                                    ref SubtitleScore score, bool addEmptyMatches = false)
        {
            if (qm1 != null && qm2 != null)
            {
                var q1 = qm1.Quality;
                var q2 = qm2.Quality;
                if (q1 != null && q2 != null)
                {
                    if (q1.Source != Parser.Qualities.Source.UNKNOWN && q1.Source == q2.Source)
                        score.AddMatch("source");

                    if (q1.Resolution > 0 && q1.Resolution == q2.Resolution)
                        score.AddMatch("resolution");

                    if (q1.Modifier == Parser.Qualities.Modifier.NONE && q1.Modifier == q2.Modifier && addEmptyMatches)
                        score.AddMatch("source_modifier");
                    else
                    if (q1.Modifier != Parser.Qualities.Modifier.NONE && q1.Modifier == q2.Modifier)
                        score.AddMatch("source_modifier");
                }

                if (qm1.AudioCodec.IsNotNullOrWhiteSpace() && qm2.AudioCodec.IsNotNullOrWhiteSpace())
                    if (qm1.AudioCodec == qm2.AudioCodec)
                        score.AddMatch("audio_codec");

                if (qm1.VideoCodec.IsNotNullOrWhiteSpace() && qm2.VideoCodec.IsNotNullOrWhiteSpace())
                    if (qm1.VideoCodec == qm2.VideoCodec)
                        score.AddMatch("video_codec");
            }

        }
    }
}