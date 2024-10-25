using MediaBrowser.Common.Extensions;
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
using subbuzz.Providers.OpenSubtitlesAPI;
using subbuzz.Providers.OpenSubtitlesAPI.Models;
using subbuzz.Providers.OpenSubtitlesAPI.Models.Responses;
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
    class OpenSubtitlesCom : ISubBuzzProvider
    {
        internal const string NAME = "opensubtitles.com";
        private const string ServerUrl = "https://www.opensubtitles.com";
        private static readonly string[] CacheRegionSub = { "opensubtitles", "sub" };
        private static readonly string[] CacheRegionSearch = { "opensubtitles", "search" };

        private readonly Logger _logger;
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

        private static FileCache? GetCacheSub()
            => GetCache(CacheRegionSub, GetOptions().Cache.SubLifeInMinutes);
        private static FileCache? GetCacheSub(int life)
            => GetCache(CacheRegionSub, life);
        private static FileCache? GetCacheSearch()
            => GetCache(CacheRegionSearch, GetOptions().Cache.SearchLifeInMinutes);
        private static FileCache? GetCacheSearch(int life)
            => GetCache(CacheRegionSearch, life);


        public class LinkSub
        {
            public string Id { get; set; } = string.Empty;
            public string Lang { get; set; } = string.Empty;
            public float? Fps { get; set; } = null;
            public float? FpsVideo { get; set; } = null;
            public bool? IsForced { get; set; } = null;
            public bool? IsSdh { get; set; } = null;

            public string GetId()
            {
                return Utils.Base64UrlEncode<LinkSub>(this);
            }

            public static LinkSub? FromId(string id)
            {
                if (id.IsNotNullOrWhiteSpace())
                    return Utils.Base64UrlDecode<LinkSub>(id);

                return null;
            }
        }

        public OpenSubtitlesCom(
            Logger logger,
            IFileSystem fileSystem,
            ILocalizationManager localizationManager,
            ILibraryManager libraryManager)
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _localizationManager = localizationManager;
            _libraryManager = libraryManager;

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString();
            OpenSubtitles.RequestHelperInstance = new RequestHelper(logger, version);
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            LinkSub? linkSub = LinkSub.FromId(id) ?? throw new FormatException($"Invalid Id: {id}");

            SubtitleResponse? subResp = GetSubtitlesFromCache(linkSub, GetOptions().Cache.GetSubLife(), out var expired);
            if (subResp != null && !expired) {
                _logger.LogDebug($"Subtitles with file id {linkSub.Id} loaded from cache");
                return subResp;
            }

            try
            {
                var link = await GetDownloadLink(linkSub.Id, cancellationToken).ConfigureAwait(false);
                if (link.IsNullOrWhiteSpace())
                {
                    throw new Exception($"Can't get download link for file ID {linkSub.Id}");
                }

                var res = await OpenSubtitles.DownloadSubtitleAsync(link, cancellationToken).ConfigureAwait(false);
                if (!res.Ok)
                {
                    throw new Exception($"Subtitle {link} could not be downloaded: {res.Code}");
                }

                if (res.Data is null)
                {
                    throw new Exception("Empty response data!");
                }

                using (Stream fileStream = new MemoryStream())
                {

                    res.Data.CopyTo(fileStream);
                    res.Data.Close();

                    string format;
                    Stream outStream = SubtitleConvert.ToSupportedFormat(
                        fileStream, linkSub.Fps, out format, 
                        GetOptions().SubEncoding.GetUtf8(), GetOptions().SubPostProcessing);
                    
                    if (GetOptions().Cache.Subtitle)
                    {
                        try
                        {
                            GetCacheSub()!.Add(linkSub.Id, fileStream);
                        }
                        catch (Exception e) 
                        {
                            _logger.LogError(e, $"Can't add subtitles to cache: {e}");
                        }
                    }

                    return new SubtitleResponse
                    {
                        Format = format,
                        Language = linkSub.Lang,
                        IsForced = linkSub.IsForced ?? false,
                        Stream = outStream,
                    };
                }
            }
            catch (Exception e)
            {
                if (subResp is not null)
                {
                    _logger.LogError(e, $"Get response from cache, as it's not able to get the response from server: {e}");
                    return subResp;
                }

                throw;
            }

        }

        protected SubtitleResponse? GetSubtitlesFromCache(LinkSub linkSub, int life, out bool expired)
        {
            expired = false;
            try
            {
                if (!GetOptions().Cache.Subtitle)
                {
                    return null;
                }

                using (Stream fileStream = GetCacheSub(life)!.Get(linkSub.Id))
                {
                    Stream outStream = SubtitleConvert.ToSupportedFormat(
                        fileStream, linkSub.Fps, out string format,
                        GetOptions().SubEncoding.GetUtf8(), GetOptions().SubPostProcessing);

                    return new SubtitleResponse
                    {
                        Format = format,
                        Language = linkSub.Lang,
                        IsForced = linkSub.IsForced ?? false,
                        Stream = outStream,
                    };
                }
            }
            catch (FileCacheItemNotFoundException)
            {
                return null;
            }
            catch (FileCacheItemExpiredException)
            {
                expired = true;
                return GetSubtitlesFromCache(linkSub, -1, out var _);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Unable to load subtitles from cache: {e}");
            }

            return null;
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var res = new List<SubtitleInfo>();

            try
            {
                var apiKey = GetOptions().OpenSubApiKey;
                if (!GetOptions().EnableOpenSubtitles)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, "{0}");

                if (si.Lang == "zh") si.Lang = "zh-cn";
                else if (si.Lang == "pt") si.Lang = "pt-pt";
                else if (si.LanguageInfo.ThreeLetterISOLanguageName == "spa") si.Lang = "es";

                _logger.LogInformation($"Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}, IMDB={si.ImdbId}");

                string hash = GetOptions().OpenSubUseHash ? CalcHash(request.MediaPath) : string.Empty;

                res = await Search(si, apiKey, hash, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Search error: {e}");
            }

            watch.Stop();
            _logger.LogInformation($"Search duration: {watch.ElapsedMilliseconds / 1000.0} sec. Subtitles found: {res.Count}");

            return res;
        }

        protected async Task<List<SubtitleInfo>> Search(SearchInfo si, string apiKey, string hash, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            var options = new Dictionary<string, string>
            {
                { "languages", si.Lang },
                { "type", si.VideoType == VideoContentType.Episode ? "episode" : "movie" },
            };

            if (si.IsForced)
            {
                options.Add("foreign_parts_only", "only");
            }

            if (hash.IsNotNullOrWhiteSpace())
            {
                options.Add("moviehash", hash);
            }

            if (si.VideoType == VideoContentType.Episode)
            {
                // Search for episodes is no longer working if id, parent_id, season_number and episode_number are all specified in one query.
                // It used to work before, but not at the moment. Currently it work if only id is specified or parent_id, season and episode.
                bool useSeasonAndEpisode = true;

                if (si.ImdbIdInt > 0)
                {
                    if (si.ImdbIdEpisodeInt > 0)
                    {
                        options.Add("imdb_id", si.ImdbIdEpisodeInt.ToString(CultureInfo.InvariantCulture));
                        useSeasonAndEpisode = false;
                    }
                    else
                    {
                        options.Add("parent_imdb_id", si.ImdbIdInt.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                if (si.TmdbId.IsNotNullOrWhiteSpace())
                {
                    if (si.TmdbIdEpisode.IsNotNullOrWhiteSpace())
                    {
                        options.Add("tmdb_id", si.TmdbIdEpisode);
                        useSeasonAndEpisode = false;
                    }
                    else
                    {
                        options.Add("parent_tmdb_id", si.TmdbId);
                    }
                }
                else
                {
                    options.Add("query", si.TitleSeries);
                }

                if (useSeasonAndEpisode)
                {
                    if (si.SeasonNumber != null) options.Add("season_number", si.SeasonNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    if (si.EpisodeNumber != null) options.Add("episode_number", si.EpisodeNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                }
            }
            else
            {
                if (si.ImdbIdInt > 0)
                    options.Add("imdb_id", si.ImdbIdInt.ToString(CultureInfo.InvariantCulture));
                else
                if (si.TmdbId.IsNotNullOrWhiteSpace())
                    options.Add("tmdb_id", si.TmdbId);
                else
                {
                    options.Add("query", si.TitleMovie);
                    if ((si.Year ?? 0) > 0)
                    {
                        options["query"] += $" ({si.Year ?? 0})";
                        options.Add("year", $"{ si.Year ?? 0}");
                    }
                }
            }

            foreach (var opt in options)
                options[opt.Key] = opt.Value.ToLowerInvariant();

            _logger.LogDebug($"Search options: {string.Join(" ", options)}");

            var searchResponse = await SearchCachedAsync(options, apiKey, cancellationToken).ConfigureAwait(false);

            if (!searchResponse.Ok)
            {
                _logger.LogInformation($"Invalid response: {searchResponse.Code} - {searchResponse.Body}");
                return res;
            }

            if (searchResponse.Data is null)
            {
                _logger.LogInformation("Response data is null");
                return res;
            }

            foreach (ResponseData resItem in searchResponse.Data)
            {
                var subItem = resItem.Attributes;

                string itemTitle = $"{subItem.FeatureDetails.Title ?? subItem.FeatureDetails.ParentTitle}";
                if (subItem.FeatureDetails.Year != null) itemTitle += $" ({subItem.FeatureDetails.Year})";

                string subInfo = $"{itemTitle}<br>{subItem.Release}";
                subInfo += (subItem.Comments.IsNotNullOrWhiteSpace()) ? $"<br>{subItem.Comments}" : "";
                subInfo += string.Format("<br>{0} | {1}", subItem.UploadDate.ToString("g", CultureInfo.CurrentCulture), subItem.Uploader.Name);
                if ((subItem.Fps ?? 0.0) > 0) subInfo += $" | {subItem.Fps?.ToString(CultureInfo.InvariantCulture)}";

                var subScoreBase = new SubtitleScore();
                si.MatchTitle(itemTitle, ref subScoreBase);
                si.MatchImdbId(subItem.FeatureDetails.ImdbId, ref subScoreBase);
                si.MatchImdbId(subItem.FeatureDetails.ParentImdbId, ref subScoreBase);
                si.MatchFps(subItem.Fps, ref subScoreBase);
                si.MatchYear(subItem.FeatureDetails.Year, ref subScoreBase);

                if (subItem.Files.Count == 1)
                {
                    si.MatchTitle(subItem.Release, ref subScoreBase);
                }

                foreach (var file in subItem.Files)
                {
                    bool ignorMutliDiscSubs = subItem.Files.Count > 1;
                    float score = si.CaclScore(file.FileName, subScoreBase, false, ignorMutliDiscSubs);
                    if ((score == 0 || score < GetOptions().MinScore) && ((subItem.MovieHashMatch ?? false) == false))
                    {
                        _logger.LogInformation($"Ignore file: {file.FileName ?? ""} ID: {file.FileId} Score: {score}");
                        continue;
                    }

                    var item = new SubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = GetId(file.FileId, si.GetLanguageTag(), subItem.Fps, si.VideoFps, subItem.ForeignPartsOnly, subItem.HearingImpaired),
                        ProviderName = Name,
                        Name = file.FileName ?? "...",
                        PageLink = subItem.Url,
                        Format = SubtitleConvert.GetExtSupportedByEmby(subItem.Format),
                        Author = subItem.Uploader.Name,
                        Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                        DateCreated = subItem.UploadDate,
                        CommunityRating = subItem.Ratings,
                        DownloadCount = subItem.DownloadCount,
                        IsHashMatch = (score >= GetOptions().HashMatchByScore) || (subItem.MovieHashMatch ?? false),
                        IsForced = subItem.ForeignPartsOnly,
                        IsHearingImpaired = subItem.HearingImpaired,
                        AiTranslated = subItem.AiTranslated,
                        MachineTranslated = subItem.MachineTranslated,
                        Score = score,
                    };

                    res.Add(item);
                }
            }

            return res;
        }

        public async Task<ApiResponse<IReadOnlyList<ResponseData>>> SearchCachedAsync(Dictionary<string, string> options, string apiKey, CancellationToken cancellationToken)
        {
            string cacheKey = string.Join(",", options);
            bool expiredFound = false;

            if (GetOptions().Cache.Search)
            {
                try
                {
                    using var stream = GetCacheSearch()!.Get(cacheKey);
                    return JsonSerializer.Deserialize<ApiResponse<IReadOnlyList<ResponseData>>>(stream) ?? throw new Exception("Cache deserialization return null!");
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
                    _logger.LogError(e, $"Unable to load subtitles from cache: {e}");
                }
            }

            var resp = await OpenSubtitles.SearchSubtitlesAsync(options, apiKey, cancellationToken).ConfigureAwait(false);

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
                return JsonSerializer.Deserialize<ApiResponse<IReadOnlyList<ResponseData>>>(stream) ?? throw new Exception("Cache deserialization return null!");
            }

            return resp;
        }

        private async Task Login(CancellationToken cancellationToken)
        {
            var apiKey = GetOptions().OpenSubApiKey;
            var userName = GetOptions().OpenSubUserName;
            var password = GetOptions().OpenSubPassword;

            if (userName.IsNullOrWhiteSpace() || password.IsNullOrWhiteSpace())
            {
                throw new AuthenticationException("Account username and/or password are not set up");
            }

            var loginResponse = await OpenSubtitlesAPI.OpenSubtitles.LogInAsync(
                userName, password, apiKey, cancellationToken).ConfigureAwait(false);

            if (!loginResponse.Ok)
                GetOptions().OpenSubToken = string.Empty;
            else
                GetOptions().OpenSubToken = loginResponse.Data?.Token ?? string.Empty;

            SaveOptions();

            if (GetOptions().OpenSubToken.IsNullOrWhiteSpace())
            {
                _logger.LogInformation($"Login failed: {loginResponse.Code} - {loginResponse.Body}");
                throw new AuthenticationException("Authentication to OpenSubtitles.com failed.");
            }
        }

        private async Task<string> GetDownloadLink(string fileId, CancellationToken cancellationToken)
        {
            var apiKey = GetOptions().OpenSubApiKey;
            var token = GetOptions().OpenSubToken;

            if (token.IsNullOrWhiteSpace())
            {
                await Login(cancellationToken).ConfigureAwait(false);
                token = GetOptions().OpenSubToken;
            }

            var fid = int.Parse(fileId, CultureInfo.InvariantCulture);
            var link = await OpenSubtitles.GetSubtitleLinkAsync(fid, token, apiKey, cancellationToken).ConfigureAwait(false);

            if (link.Ok)
            {
                return link.Data?.Link ?? string.Empty;
            }
            else
            {
                switch (link.Code)
                {
                    case HttpStatusCode.NotAcceptable:
                        throw new RateLimitExceededException("OpenSubtitles.com download limit reached.");

                    case HttpStatusCode.Unauthorized:
                        _logger.LogInformation($"JWT token expired, obtain a new one and try again");

                        GetOptions().OpenSubToken = string.Empty;
                        SaveOptions();

                        return await GetDownloadLink(fileId, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation($"Invalid response for file {fid}: {link.Code}\n\n{link.Body}");
            }

            return string.Empty;
        }

        private string CalcHash(string path)
        {
            try
            {
                using (var fileStream = System.IO.File.OpenRead(path))
                {
                    return Hash.ComputeHash(fileStream);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Exception while computing hash for {path}");
            }

            return string.Empty;
        }

        private static string GetId(int fileId, string lang, float? fps, float? fpsVide, bool? isForced, bool? isSdh)
        {
            var link = new LinkSub 
            { 
                Id = $"{fileId}", 
                Lang = lang, 
                Fps = fps, 
                FpsVideo = fpsVide,
                IsForced = isForced,
                IsSdh = isSdh,
            };
            return link.GetId();
        }

    }
}
