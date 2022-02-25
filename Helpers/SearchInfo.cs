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
using System.IO;
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
        public string TmdbId = "";
        public string TmdbIdEpisode = "";
        public string Lang = ""; // two letter lower case language code
        public CultureDto LanguageInfo;
        public VideoContentType VideoType;
        public int? SeasonNumber = null;
        public int? EpisodeNumber = null;
        public int? Year = null;
        public int? SeriesYear = null;
        public string TitleMovie = "";
        public string TitleSeries = "";
        public string FileName = "";
        public Parser.EpisodeInfo EpInfo = null;
        public Parser.MovieInfo MvInfo = null;
        public bool IsForced = false;

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
            res.IsForced = request.IsForced ?? false;
#else
            res.LanguageInfo = localize.FindLanguageInfo(request.Language);
#endif
            res.Lang = res.LanguageInfo.TwoLetterISOLanguageName.ToLower();

            BaseItem libItem = lib.FindByPath(request.MediaPath, false);
            if (libItem == null)
            {
                return res;
            }

            res.FileName = Path.GetFileNameWithoutExtension(libItem.Path);

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
                mv.ProviderIds.TryGetValue("Tmdb", out res.TmdbId);

                res.TitleMovie = res.SearchText;
                res.MvInfo = Parser.Movie.ParsePath(mv.Path);
                if (res.MvInfo != null && res.MvInfo.Year == 0)
                    res.MvInfo.Year = request.ProductionYear ?? 0;
            }
            else
            if (res.VideoType == VideoContentType.Episode && episode_format.IsNotNullOrWhiteSpace())
            {
                res.SeasonNumber = request.ParentIndexNumber;
                res.EpisodeNumber = request.IndexNumber;

                Episode ep = libItem as Episode;
                MediaStream media = ep.GetDefaultVideoStream();
                if (media != null) res.VideoFps = media.AverageFrameRate;

                string title = !string.IsNullOrEmpty(ep.Series.OriginalTitle) ? ep.Series.OriginalTitle : ep.Series.Name;
                if (inconsistentTvs != null)
                {
                    title = inconsistentTvs.Aggregate(title, (current, value) =>
                        Regex.Replace(current, Regex.Escape(value.Key), value.Value, RegexOptions.IgnoreCase));
                }

                // episode format {0} - series name, {1} - season, {2} - episode
                if (res.SeasonNumber != null && res.EpisodeNumber != null)
                    res.SearchText = string.Format(episode_format,
                        title,
                        res.SeasonNumber,
                        res.EpisodeNumber);

                // season format {0} - series name, {1} - season
                if (res.SeasonNumber != null && season_format.IsNotNullOrWhiteSpace())
                    res.SearchSeason = string.Format(season_format,
                        title,
                        res.SeasonNumber);

                string titleEp = !string.IsNullOrEmpty(ep.OriginalTitle) ? ep.OriginalTitle : ep.Name;
                if (titleEp.ContainsIgnoreCase(title)) res.SearchEpByName = titleEp;
                else res.SearchEpByName = string.Format("{0} {1}", title, titleEp);

                ep.Series.ProviderIds.TryGetValue("Imdb", out res.ImdbId);
                ep.Series.ProviderIds.TryGetValue("Tmdb", out res.TmdbId);
                ep.ProviderIds.TryGetValue("Imdb", out res.ImdbIdEpisode);
                ep.ProviderIds.TryGetValue("Tmdb", out res.TmdbIdEpisode);

                res.TitleSeries = title;
                res.TitleMovie = res.SearchEpByName;
                res.EpInfo = Parser.Episode.ParsePath(ep.Path);
                if (res.EpInfo != null && res.EpInfo.SeriesTitleInfo.Year == 0)
                    res.EpInfo.SeriesTitleInfo.Year = request.ProductionYear ?? 0;

                res.SeriesYear = ep.Series.ProductionYear;
            }

            var regexImdbId = new Regex(@"tt(\d+)");

            if (!string.IsNullOrWhiteSpace(res.ImdbId))
            {
                var match = regexImdbId.Match(res.ImdbId);
                if (match.Success && match.Groups.Count > 1)
                    res.ImdbIdInt = int.Parse(match.Groups[1].ToString());
            }

            if (!string.IsNullOrWhiteSpace(res.ImdbIdEpisode))
            {
                var match = regexImdbId.Match(res.ImdbIdEpisode);
                if (match.Success && match.Groups.Count > 1)
                    res.ImdbIdEpisodeInt = int.Parse(match.Groups[1].ToString());
            }

            res.Year = request.ProductionYear;

            return res;
        }

        public float CaclScore(string subFileName, SubtitleScore baseScore, bool scoreVideoFileName, bool ignorMutliDiscSubs = false)
        {
            SubtitleScore subScore = baseScore == null ? new SubtitleScore() : (SubtitleScore)baseScore.Clone();

            if (VideoType == VideoContentType.Episode)
            {
                Parser.EpisodeInfo epInfo = Parser.Episode.ParseTitle(subFileName);
                bool checkSuccess = MatchEpisode(epInfo, ref subScore);

                if (scoreVideoFileName)
                {
                    epInfo = Parser.Episode.ParseTitle(FileName);
                    if (!MatchEpisode(epInfo, ref subScore) && !checkSuccess)
                        return 0;
                }
                else
                if (!checkSuccess)
                    return 0;

                return subScore.CalcScoreEpisode();
            }
            else
            if (VideoType == VideoContentType.Movie)
            {
                Parser.MovieInfo mvInfo = Parser.Movie.ParseTitle(subFileName, true);

                if (mvInfo != null && ignorMutliDiscSubs && mvInfo.Cd > 0)
                    return 0;

                MatchMovie(mvInfo, ref subScore, true);

                if (scoreVideoFileName)
                {
                    mvInfo = Parser.Movie.ParseTitle(FileName, true);
                    MatchMovie(mvInfo, ref subScore, true);
                }

                return subScore.CalcScoreMovie();
            }

            return 0;
        }

        public void MatchTitle(string title, ref SubtitleScore score)
        {
            if (VideoType == VideoContentType.Episode)
            {
                Parser.EpisodeInfo epInfo = Parser.Episode.ParseTitle(title);
                if (epInfo == null) 
                {
                    var seriesTitleInfo = Parser.Episode.GetSeriesTitleInfo(title);
                    MatchTitleSimple(seriesTitleInfo.TitleWithoutYear, ref score);
                    MatchYear(seriesTitleInfo.Year, ref score);
                }
                else MatchEpisode(epInfo, ref score);
            }
            else
            if (VideoType == VideoContentType.Movie)
            {
                Parser.MovieInfo mvInfo = Parser.Movie.ParseTitle(title, false);
                MatchMovie(mvInfo, ref score, true);
            }
        }

        public void MatchTitleSimple(string title, ref SubtitleScore score)
        {

            if (VideoType == VideoContentType.Episode)
            {
                string titleNorm = Parser.Episode.NormalizeTitle(title);
                if (titleNorm == Parser.Episode.NormalizeTitle(TitleSeries) ||
                    titleNorm == Parser.Episode.NormalizeTitle(SearchEpByName))
                {
                    score.AddMatch("title");
                }
            }
            else
            if (VideoType == VideoContentType.Movie)
            {
                string titleNorm = Parser.Movie.NormalizeTitle(title);
                if (titleNorm == Parser.Movie.NormalizeTitle(TitleMovie))
                {
                    score.AddMatch("title");
                }
            }
        }

        public void MatchYear(int? year, ref SubtitleScore score)
        {
            if (year == null) return;
            if (Year != null && Year == year)
                score.AddMatch("year");

            if (VideoType == VideoContentType.Episode)
            {
                if (SeriesYear != null && SeriesYear == year)
                    score.AddMatch("year");
            }
        }

        public void MatchYear(string yearStr, ref SubtitleScore score)
        {
            if (yearStr != null && int.TryParse(yearStr, NumberStyles.Number, CultureInfo.InvariantCulture, out int yearNum))
            {
                MatchYear(yearNum, ref score);
            }
        }

        public bool MatchImdbId(int? imdb, ref SubtitleScore score)
        {
            if ((imdb ?? 0) > 0)
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

        public bool MatchFps(string fpsText, ref SubtitleScore score)
        {
            try
            {
                float fps = float.Parse(fpsText, CultureInfo.InvariantCulture);
                return MatchFps(fps, ref score);
            }
            catch
            {
            }

            return true; // no frame rate information
        }

        public bool MatchFps(float? fps, ref SubtitleScore score)
        {
            if ((fps ?? 0) > 0 && VideoFps != null && VideoFps > 0)
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

        public bool MatchEpisode(Parser.EpisodeInfo epInfo, ref SubtitleScore score)
        {
            if (epInfo == null)
            {
                return true; // no enough info to decide if the episode should be skipped
            }

            bool season = true;
            bool episode = true;

            MatchTitleSimple(epInfo.SeriesTitleInfo.TitleWithoutYear, ref score);
            MatchYear(epInfo.SeriesTitleInfo.Year, ref score);

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

        public bool MatchMovie(Parser.MovieInfo mvInfo, ref SubtitleScore score, bool addEmptyMatches = false)
        {
            if (mvInfo == null)
            {
                return true; // no enough info to decide if the movie should be skipped
            }

            MatchTitleSimple(mvInfo.MovieTitle, ref score);
            MatchYear(mvInfo.Year, ref score);

            if (MvInfo != null)
            {
                if (mvInfo.ReleaseGroup.IsNotNullOrWhiteSpace() &&
                    MvInfo.ReleaseGroup.IsNotNullOrWhiteSpace() &&
                    mvInfo.ReleaseGroup.EqualsIgnoreCase(MvInfo.ReleaseGroup))
                {
                    score.AddMatch("release_group");
                }

                MatchEdition(mvInfo.Edition, MvInfo.Edition, ref score, addEmptyMatches);
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

        protected void MatchEdition(string ed1, string ed2, ref SubtitleScore score, bool addEmptyMatches = false)
        {
            if (ed1.IsNullOrWhiteSpace() &&
                ed2.IsNullOrWhiteSpace())
            {
                if (addEmptyMatches)
                    score.AddMatch("edition");
            }
            else
            if (ed1.IsNullOrWhiteSpace() ||
                ed2.IsNullOrWhiteSpace())
            {
                return;
            }

            ed1 = ed1.Replace("'", string.Empty);
            ed2 = ed2.Replace("'", string.Empty);

            if (ed1.Equals(ed2, StringComparison.OrdinalIgnoreCase))
            {
                score.AddMatch("edition");
            }

        }
    }
}
