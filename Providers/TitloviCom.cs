using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Configuration;
using subbuzz.Extensions;
using subbuzz.Helpers;
using subbuzz.Providers.Http;
using subbuzz.Providers.TitloviAPI;
using subbuzz.Providers.TitloviAPI.Models;
using subbuzz.Providers.TitloviAPI.Models.Responses;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.Providers
{
    public class TitloviCom : ISubBuzzProvider
    {
        internal const string NAME = "titlovi.com";
        private const int MaxPages = 10;
        private static readonly string[] CacheRegionSub = { "titlovi", "sub" };
        private static readonly string[] CacheRegionSearch = { "titlovi", "search" };

        private readonly Logger _logger;
        private readonly Http.Download _downloader;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;

        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        private static PluginConfiguration GetOptions()
            => Plugin.Instance!.Configuration;
        private static void SaveOptions()
            => Plugin.Instance!.SaveConfiguration();

        private static FileCache? GetCache(string[] region, int life = 0)
            => Plugin.Instance?.Cache?.FromRegion(region, life);

        private static FileCache? GetCacheSearch()
            => GetCache(CacheRegionSearch, GetOptions().Cache.SearchLifeInMinutes);

        private static FileCache? GetCacheSearch(int life)
            => GetCache(CacheRegionSearch, life);

        public TitloviCom(
            Logger logger,
            IFileSystem fileSystem,
            ILocalizationManager localizationManager,
            ILibraryManager libraryManager)
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _localizationManager = localizationManager;
            _libraryManager = libraryManager;
            _downloader = new Http.Download(logger);

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString();
            Titlovi.RequestHelperInstance = new RequestHelper(logger, version);
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            return await _downloader.GetSubtitles(id, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var res = new List<SubtitleInfo>();

            try
            {
                if (!GetOptions().EnableTitloviCom)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, "{0} S{1:D2}E{2:D2}");

                _logger.LogInformation($"Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}, IMDB={si.ImdbId}");

                if (!Titlovi.IsLanguageSupported(si.Lang))
                {
                    _logger.LogInformation($"Language '{si.Lang}' is not available on {NAME}");
                    return res;
                }

                string query = si.VideoType == VideoContentType.Episode ? si.TitleSeries : si.SearchText;
                if (query.IsNullOrWhiteSpace())
                {
                    return res;
                }

                res = await Search(si, query, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Search error: {e}");
            }

            watch.Stop();
            _logger.LogInformation($"Search duration: {watch.ElapsedMilliseconds / 1000.0} sec. Subtitles found: {res.Count}");

            return res;
        }

        // Season or episode 0 marks a season pack, which may contain the requested episode.
        public static bool IsMatchingEpisode(SubtitleResult sub, int? season, int? episode)
        {
            if (season != null && (sub.Season ?? 0) > 0 && sub.Season != season)
            {
                return false;
            }

            if (episode != null && (sub.Episode ?? 0) > 0 && sub.Episode != episode)
            {
                return false;
            }

            return true;
        }

        public static string GetTitle(SubtitleResult sub, bool isEpisode)
        {
            string title = (sub.Title ?? string.Empty).Trim();

            if ((sub.Year ?? 0) > 0)
            {
                title += $" ({sub.Year})";
            }

            if (isEpisode && (sub.Season ?? 0) > 0)
            {
                title += string.Format(CultureInfo.InvariantCulture, " S{0:D2}", sub.Season);
                if ((sub.Episode ?? 0) > 0)
                {
                    title += string.Format(CultureInfo.InvariantCulture, "E{0:D2}", sub.Episode);
                }
            }

            return title;
        }

        protected async Task<List<SubtitleInfo>> Search(SearchInfo si, string query, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            bool isEpisode = si.VideoType == VideoContentType.Episode;
            string imdbId = si.ImdbIdInt > 0 ? si.ImdbId : string.Empty;

            var subs = await SearchAllPages(si, query, isEpisode, imdbId, cancellationToken).ConfigureAwait(false);

            if (subs.Count == 0 && imdbId.IsNotNullOrWhiteSpace())
            {
                // the title may be listed without the IMDb ID, so retry by title only
                subs = await SearchAllPages(si, query, isEpisode, string.Empty, cancellationToken).ConfigureAwait(false);
            }

            foreach (var sub in subs)
            {
                try
                {
                    res.AddRange(await ProcessSubtitle(sub, si, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Subtitle ID {sub.Id}: {e}");
                }
            }

            return res;
        }

        private async Task<List<SubtitleResult>> SearchAllPages(SearchInfo si, string query, bool isEpisode, string imdbId, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleResult>();
            int page = 1;
            int pagesAvailable;

            do
            {
                var options = Titlovi.BuildSearchOptions(query, si.Lang, isEpisode, si.SeasonNumber, si.EpisodeNumber, imdbId, page);

                _logger.LogDebug($"Search options: {Titlovi.BuildQueryString(string.Empty, options)}");

                var response = await SearchCachedAsync(options, cancellationToken).ConfigureAwait(false);

                if (!response.Ok || response.Data == null)
                {
                    _logger.LogInformation($"Invalid response: {response.Code} - {response.Body}");
                    break;
                }

                res.AddRange(response.Data.SubtitleResults);

                pagesAvailable = response.Data.PagesAvailable;
                page = Math.Max(page, response.Data.CurrentPage) + 1;
            }
            while (page <= pagesAvailable && page <= MaxPages);

            return res;
        }

        protected async Task<List<SubtitleInfo>> ProcessSubtitle(SubtitleResult sub, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();
            if (sub == null || sub.Id <= 0)
            {
                return res;
            }

            bool isEpisode = si.VideoType == VideoContentType.Episode;

            if (Titlovi.GetLanguageCode(sub.Lang) != si.Lang)
            {
                _logger.LogDebug($"Ignore subtitle ID {sub.Id}, language '{sub.Lang}' is not the requested one");
                return res;
            }

            if (isEpisode && !IsMatchingEpisode(sub, si.SeasonNumber, si.EpisodeNumber))
            {
                return res;
            }

            string title = GetTitle(sub, isEpisode);

            var subScoreBase = new SubtitleScore();
            si.MatchTitle(title, ref subScoreBase);
            si.MatchYear(sub.Year, ref subScoreBase);
            if (sub.Release.IsNotNullOrWhiteSpace())
            {
                si.MatchTitle($"{title} {sub.Release}", ref subScoreBase);
            }

            DateTime? dt = Titlovi.ParseDate(sub.Date);

            string subInfo = title;
            if (sub.Release.IsNotNullOrWhiteSpace())
            {
                subInfo += $"<br>{sub.Release}";
            }

            var details = new List<string>();
            if (dt != null) details.Add(dt.Value.ToString("g", CultureInfo.CurrentCulture));
            if (sub.Lang.IsNotNullOrWhiteSpace()) details.Add(sub.Lang);
            if (details.Count > 0) subInfo += "<br>" + string.Join(" | ", details);

            int type = sub.Type > 0 ? sub.Type : (isEpisode ? Titlovi.TypeEpisode : Titlovi.TypeMovie);

            var link = new Http.RequestSub
            {
                Url = Titlovi.GetDownloadUrl(sub.Id, type),
                Referer = Titlovi.ServerUrl,
                Type = Http.RequestType.GET,
                CacheRegion = CacheRegionSub,
                CacheLifespan = GetOptions().Cache.GetSubLife(),
                Lang = si.GetLanguageTag(),
                FpsVideo = si.VideoFps,
            };

            using (var files = await _downloader.GetArchiveFiles(link, cancellationToken).ConfigureAwait(false))
            {
                int subFilesCount = files.SubCount;
                foreach (var file in files)
                {
                    if (!file.IsSubfile())
                    {
                        _logger.LogDebug($"Ignoring '{file.Name}' as it's not a subtitle file. Link: {link.Url}");
                        continue;
                    }

                    link.File = file.Name;
                    link.Fps = file.Sub.FpsDetected;

                    SubtitleScore subScore = (SubtitleScore)subScoreBase.Clone();
                    si.MatchFps(link.Fps, ref subScore);

                    bool scoreVideoFileName = subFilesCount == 1 && si.FileName.IsNotNullOrWhiteSpace() && sub.Release.ContainsIgnoreCase(si.FileName);
                    bool ignorMutliDiscSubs = subFilesCount > 1;

                    float score = si.CaclScore(file.Name, subScore, scoreVideoFileName, ignorMutliDiscSubs);
                    if (score == 0 || score < GetOptions().MinScore)
                    {
                        _logger.LogInformation($"Ignore file: {file.Name} ID: {sub.Id} Score: {score}");
                        continue;
                    }

                    string subComment = subInfo;
                    if (link.Fps != null) subComment += $" | {link.Fps?.ToString(CultureInfo.InvariantCulture)}";
                    subComment += " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %";

                    var item = new SubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = link.GetId(),
                        ProviderName = Name,
                        Name = file.Name,
                        PageLink = Titlovi.GetPageUrl(sub.Title, sub.Id),
                        Format = file.GetExtSupportedByEmby(),
                        Author = string.Empty,
                        Comment = subComment,
                        DateCreated = dt,
                        CommunityRating = sub.Rating,
                        DownloadCount = sub.DownloadCount,
                        IsHashMatch = score >= GetOptions().HashMatchByScore,
                        IsForced = null,
                        IsHearingImpaired = null,
                        Score = score,
                    };

                    res.Add(item);
                }
            }

            return res;
        }

        private async Task<ApiResponse<SearchResponse>> SearchCachedAsync(List<KeyValuePair<string, string>> options, CancellationToken cancellationToken)
        {
            string cacheKey = Titlovi.BuildQueryString("search", options);
            bool expiredFound = false;

            if (GetOptions().Cache.Search)
            {
                try
                {
                    using var stream = GetCacheSearch()!.Get(cacheKey);
                    return JsonSerializer.Deserialize<ApiResponse<SearchResponse>>(stream) ?? throw new Exception("Cache deserialization return null!");
                }
                catch (FileCacheItemNotFoundException)
                {
                }
                catch (FileCacheItemExpiredException)
                {
                    expiredFound = true;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Unable to load search results from cache: {e}");
                }
            }

            var resp = await SearchWithLoginAsync(options, cancellationToken).ConfigureAwait(false);

            if (resp.Ok && GetOptions().Cache.Search)
            {
                try
                {
                    using var stream = new MemoryStream();
                    JsonSerializer.Serialize(stream, resp);
                    GetCacheSearch()!.Add(cacheKey, stream);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Unable to add search response to cache: {e}");
                }
            }
            else
            if (expiredFound)
            {
                using var stream = GetCacheSearch(-1)!.Get(cacheKey);
                return JsonSerializer.Deserialize<ApiResponse<SearchResponse>>(stream) ?? throw new Exception("Cache deserialization return null!");
            }

            return resp;
        }

        private async Task<ApiResponse<SearchResponse>> SearchWithLoginAsync(List<KeyValuePair<string, string>> options, CancellationToken cancellationToken)
        {
            var (token, userId) = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            var response = await Titlovi.SearchAsync(options, token, userId, cancellationToken).ConfigureAwait(false);

            if (response.Code == HttpStatusCode.Unauthorized)
            {
                _logger.LogInformation("Token rejected, obtain a new one and try again");

                (token, userId) = await LoginAsync(cancellationToken).ConfigureAwait(false);
                response = await Titlovi.SearchAsync(options, token, userId, cancellationToken).ConfigureAwait(false);
            }

            return response;
        }

        private async Task<(string, int)> GetTokenAsync(CancellationToken cancellationToken)
        {
            var token = GetOptions().TitloviToken;
            var userId = GetOptions().TitloviUserId;

            if (token.IsNotNullOrWhiteSpace() && userId > 0 && !Titlovi.IsTokenExpired(GetOptions().TitloviTokenExpiration))
            {
                return (token, userId);
            }

            return await LoginAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<(string, int)> LoginAsync(CancellationToken cancellationToken)
        {
            var userName = GetOptions().TitloviUserName;
            var password = GetOptions().TitloviPassword;

            if (userName.IsNullOrWhiteSpace() || password.IsNullOrWhiteSpace())
            {
                throw new AuthenticationException("Account username and/or password are not set up");
            }

            var loginResponse = await Titlovi.LogInAsync(userName, password, cancellationToken).ConfigureAwait(false);
            var loginInfo = loginResponse.Ok ? loginResponse.Data : null;

            GetOptions().TitloviToken = loginInfo?.Token ?? string.Empty;
            GetOptions().TitloviUserId = loginInfo?.UserId ?? 0;
            GetOptions().TitloviTokenExpiration = loginInfo?.ExpirationDate ?? string.Empty;

            SaveOptions();

            if (GetOptions().TitloviToken.IsNullOrWhiteSpace())
            {
                _logger.LogInformation($"Login failed: {Titlovi.GetLoginErrorMessage(loginResponse)}");
                throw new AuthenticationException("Authentication to titlovi.com failed.");
            }

            return (GetOptions().TitloviToken, GetOptions().TitloviUserId);
        }

    }
}
