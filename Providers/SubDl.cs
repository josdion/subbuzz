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
using subbuzz.Providers.SubdlApi;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using subbuzz.Providers.SubdlApi.Models;
using subbuzz.Providers.SubdlApi.Models.Responses;

namespace subbuzz.Providers
{
    public class SubDl : ISubBuzzProvider
    {
        internal const string NAME = "subdl.com";
        private const string ServerUrl = "https://subdl.com";
        private const string DownloadUrl = "https://dl.subdl.com";
        private static readonly string[] CacheRegionSub = { "subdl", "sub" };
        private static readonly string[] CacheRegionSearch = { "subdl", "search" };

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

        private static FileCache? GetCache(string[] region, int life = 0)
            => Plugin.Instance?.Cache?.FromRegion(region, life);

        private static FileCache? GetCacheSearch()
            => GetCache(CacheRegionSearch, GetOptions().Cache.SearchLifeInMinutes);

        private static FileCache? GetCacheSearch(int life)
            => GetCache(CacheRegionSearch, life);

        public SubDl(
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
            SubDlApi.RequestHelperInstance = new RequestHelper(logger, version);
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
                var apiKey = GetOptions().SubdlApiKey;
                if (!GetOptions().EnableSubdlCom || apiKey.IsNullOrWhiteSpace())
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, "{0}");

                if (si.Lang == "pt-br") si.Lang = "br_pt";

                _logger.LogInformation($"Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}, IMDB={si.ImdbId}");

                res = await Search(si, apiKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Search error: {e}");
            }

            watch.Stop();
            _logger.LogInformation($"Search duration: {watch.ElapsedMilliseconds / 1000.0} sec. Subtitles found: {res.Count}");

            return res;
        }

        protected async Task<List<SubtitleInfo>> Search(SearchInfo si, string apiKey, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            var options = new Dictionary<string, string>
            {
                { "languages", si.Lang },
                { "type", si.VideoType == VideoContentType.Episode ? "tv" : "movie" },
                { "subs_per_page", "30" }, // limit of subtitles will see in the results default is 10, (max can be 30)
                { "comment", "1" }, // send comment=1 to get author comment on subtitle
                { "releases", "1" },  // send releases=1 to get releases list on subtitle

                // hi (optional): send hi=1 to get is Hearing Impaired on subtitle
                // full_season (optional): send full_season = 1 to get all full season subtitles
            };

            if (si.VideoType == VideoContentType.Episode)
            {
                bool useSeasonAndEpisode = true;

                if (si.ImdbIdInt > 0)
                {
                    options.Add("imdb_id", si.ImdbId);
                }
                else
                if (si.TmdbId.IsNotNullOrWhiteSpace())
                {
                    options.Add("tmdb_id", si.TmdbId);
                }
                else
                {
                    options.Add("film_name", si.TitleSeries);
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
                    options.Add("imdb_id", si.ImdbId);
                else
                if (si.TmdbId.IsNotNullOrWhiteSpace())
                    options.Add("tmdb_id", si.TmdbId);
                else
                {
                    options.Add("film_name", si.TitleMovie);
                    if ((si.Year ?? 0) > 0)
                    {
                        options.Add("year", $"{si.Year ?? 0}");
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

            if (!searchResponse.Data.Status)
            {
                _logger.LogInformation($"Response status is FALSE: {searchResponse.Code} - {searchResponse.Body}");
                return res;
            }

            if (searchResponse.Data.Results.Count <= 0) {
                _logger.LogInformation($"Response result missing: {searchResponse.Code} - {searchResponse.Body}");
                return res;
            }

            var resItem = searchResponse.Data.Results[0];

            bool typeMatch;
            if (si.VideoType == VideoContentType.Episode)
                typeMatch = resItem.Type == "tv";
            else
                typeMatch = resItem.Type == "movie";

            if (!typeMatch)
            {
                _logger.LogInformation($"Skip worng type: {searchResponse.Code} - {searchResponse.Body}");
                return res;
            }

            var subScoreBase = new SubtitleScore();
            si.MatchTitle(resItem.Name, ref subScoreBase);
            si.MatchImdbId(resItem.ImdbId, ref subScoreBase);
            si.MatchYear(resItem.Year, ref subScoreBase);

            foreach (var subItem in searchResponse.Data.Subtitles)
            {
                subItem.Release = subItem.Release.Trim();
                SubtitleScore subScore = (SubtitleScore)subScoreBase.Clone();
                si.MatchTitle(subItem.Release, ref subScore);

                string subInfoBase = ((resItem.Year ?? 0) > 0) ?  
                    $"{resItem.Name} ({resItem.Year})<br>{subItem.Release}" : $"{resItem.Name}<br>{subItem.Release}";

                // remove extension from subtitle download link
                int fileExtPos = subItem.Url.LastIndexOf(".");
                if (fileExtPos >= 0)
                    subItem.Url = subItem.Url.Substring(0, fileExtPos);

                var link = new Http.RequestSub
                {
                    Url = DownloadUrl + subItem.Url,
                    Referer = ServerUrl + subItem.SubPage,
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
                            _logger.LogDebug($"Ignoring '{file.Name}' as it's not a subtitle file. Page: {link.Referer}. Link: {link.Url}");
                            continue;
                        }

                        link.File = file.Name;
                        link.IsSdh = subItem.HearingImpaired;

                        float score = si.CaclScore(file.Name, subScore, false, false);
                        if (score == 0 || score < GetOptions().MinScore)
                        {
                            _logger.LogInformation($"Ignore file: {file.Name} Page: {link.Referer} Link: {link.Url} Score: {score}");
                            continue;
                        }

                        string subInfo = subInfoBase;
                        foreach(var relName in subItem.Releases)
                        {
                            if (relName.Trim() != subItem.Release)
                                subInfo += "<br>" + relName;
                        }
                        subInfo += (subItem.Comment.IsNotNullOrWhiteSpace()) ? $"<br>{subItem.Comment}" : "";
                        subInfo += "<br>" + subItem.Author;

                        var item = new SubtitleInfo
                        {
                            ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                            Id = link.GetId(),
                            ProviderName = Name,
                            Name = file.Name ?? "...",
                            PageLink = ServerUrl + subItem.SubPage,
                            Format = file.GetExtSupportedByEmby(),
                            Author = subItem.Author,
                            Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                            //DateCreated = subItem.UploadDate,
                            //CommunityRating = subItem.Ratings,
                            //DownloadCount = subItem.DownloadCount,
                            //IsHashMatch = (score >= GetOptions().HashMatchByScore) || (subItem.MovieHashMatch ?? false),
                            //IsForced = subItem.ForeignPartsOnly,
                            IsHearingImpaired = subItem.HearingImpaired,
                            Score = score,
                        };

                        res.Add(item);
                    }
                }
            }

            return res;
        }

        public async Task<ApiResponse<SearchResult>> SearchCachedAsync(Dictionary<string, string> options, string apiKey, CancellationToken cancellationToken)
        {
            string cacheKey = string.Join(",", options);
            bool expiredFound = false;

            if (GetOptions().Cache.Search)
            {
                try
                {
                    using var stream = GetCacheSearch()!.Get(cacheKey);
                    return JsonSerializer.Deserialize<ApiResponse<SearchResult>>(stream) ?? throw new Exception("Cache deserialization return null!");
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

            var resp = await SubDlApi.SearchSubtitlesAsync(options, apiKey, cancellationToken).ConfigureAwait(false);

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
                return JsonSerializer.Deserialize<ApiResponse<SearchResult>>(stream) ?? throw new Exception("Cache deserialization return null!");
            }

            return resp;
        }

    }
}
