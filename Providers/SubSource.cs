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
using subbuzz.Providers.SubSourceApi;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using subbuzz.Providers.SubSourceApi.Models;
using subbuzz.Providers.SubSourceApi.Models.Responses;

namespace subbuzz.Providers
{
    public class SubSource : ISubBuzzProvider
    {
        internal const string NAME = "subsource.net";
        private const string ServerUrl = "https://subsource.net";
        private const string ApiUrl = "https://api.subsource.net/api/v1";
        private static readonly string[] CacheRegionSub = { "subsource", "sub" };
        private static readonly string[] CacheRegionSearch = { "subsource", "search" };

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

        public SubSource(
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
            SubSourceApi.SubSourceApi.RequestHelperInstance = new RequestHelper(logger, version);
        }

        private static string GetSubSourceLanguageName(SearchInfo si)
        {
            // SubSource API expects full language names, not codes
            // Use the DisplayName from LanguageInfo which provides the English name
            // e.g., "English" instead of "en"
            return si.LanguageInfo.DisplayName.ToLowerInvariant();
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
                var apiKey = GetOptions().SubSourceApiKey;
                if (!GetOptions().EnableSubSource || apiKey.IsNullOrWhiteSpace())
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, "{0}");

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

            // Step 1: Search for the movie/TV show
            var searchOptions = new Dictionary<string, string>
            {
                { "searchType", si.VideoType == VideoContentType.Episode ? "imdb" : "imdb" }
            };

            // Prefer IMDb ID for searching
            if (si.ImdbIdInt > 0)
            {
                searchOptions.Add("imdb", si.ImdbId);
            }
            else
            {
                // Fall back to text search
                searchOptions["searchType"] = "text";
                searchOptions.Add("q", si.VideoType == VideoContentType.Episode ? si.TitleSeries : si.TitleMovie);
                if ((si.Year ?? 0) > 0)
                {
                    searchOptions.Add("year", $"{si.Year ?? 0}");
                }
            }

            // Add type filter
            searchOptions.Add("type", si.VideoType == VideoContentType.Episode ? "series" : "movie");

            // Add season filter for TV shows
            if (si.VideoType == VideoContentType.Episode && si.SeasonNumber != null)
            {
                searchOptions.Add("season", si.SeasonNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            }

            _logger.LogDebug($"Movie search options: {string.Join(" ", searchOptions)}");

            var movieSearchResponse = await SearchMovieCachedAsync(searchOptions, apiKey, cancellationToken).ConfigureAwait(false);

            if (!movieSearchResponse.Ok || movieSearchResponse.Data == null || !movieSearchResponse.Data.Success)
            {
                _logger.LogInformation($"Movie search failed: {movieSearchResponse.Code} - {movieSearchResponse.Body}");
                return res;
            }

            if (movieSearchResponse.Data.Data.Count == 0)
            {
                _logger.LogInformation("No movies found in search results");
                return res;
            }

            var movie = movieSearchResponse.Data.Data[0];
            _logger.LogDebug($"Found movie: {movie.Title} ({movie.ReleaseYear}) - ID: {movie.MovieId}");

            // Step 2: Get subtitles for the movie
            var subtitleOptions = new Dictionary<string, string>
            {
                { "movieId", movie.MovieId.ToString(CultureInfo.InvariantCulture) },
                { "language", GetSubSourceLanguageName(si) },
                { "sort", "newest" }
            };

            _logger.LogDebug($"Subtitle search options: {string.Join(" ", subtitleOptions)}");

            var subtitleResponse = await SearchSubtitlesCachedAsync(subtitleOptions, apiKey, cancellationToken).ConfigureAwait(false);

            if (!subtitleResponse.Ok || subtitleResponse.Data == null || !subtitleResponse.Data.Success)
            {
                _logger.LogInformation($"Subtitle search failed: {subtitleResponse.Code} - {subtitleResponse.Body}");
                return res;
            }

            if (subtitleResponse.Data.Data.Count == 0)
            {
                _logger.LogInformation("No subtitles found");
                return res;
            }

            var subScoreBase = new SubtitleScore();
            si.MatchTitle(movie.Title, ref subScoreBase);
            si.MatchImdbId(movie.ImdbId, ref subScoreBase);
            si.MatchYear(movie.ReleaseYear, ref subScoreBase);

            // Step 3: Process each subtitle
            foreach (var subItem in subtitleResponse.Data.Data)
            {
                SubtitleScore subScore = (SubtitleScore)subScoreBase.Clone();
                
                // Match against release info
                string releaseInfoStr = string.Join(" ", subItem.ReleaseInfo);
                si.MatchTitle(releaseInfoStr, ref subScore);

                string subInfoBase = ((movie.ReleaseYear ?? 0) > 0) ?
                    $"{movie.Title} ({movie.ReleaseYear})<br>{releaseInfoStr}" : $"{movie.Title}<br>{releaseInfoStr}";

                // Build download URL - use API endpoint with authentication
                var link = new Http.RequestSub
                {
                    Url = $"{ApiUrl}/subtitles/{subItem.SubtitleId}/download",
                    Referer = ServerUrl,
                    Type = Http.RequestType.GET,
                    CacheRegion = CacheRegionSub,
                    CacheLifespan = GetOptions().Cache.GetSubLife(),
                    Lang = si.GetLanguageTag(),
                    FpsVideo = si.VideoFps,
                    Headers = new Dictionary<string, string> { { "X-API-Key", apiKey } }
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
                        link.IsSdh = subItem.HearingImpaired;

                        float score = si.CaclScore(file.Name, subScore, false, false);
                        if (score == 0 || score < GetOptions().MinScore)
                        {
                            _logger.LogInformation($"Ignore file: {file.Name} Link: {link.Url} Score: {score}");
                            continue;
                        }

                        string subInfo = subInfoBase;
                        if (subItem.Commentary.IsNotNullOrWhiteSpace())
                        {
                            var comment = Utils.TrimString(subItem.Commentary, "\n").Replace("\n\n", "\n").Replace("\n\n", "\n").Replace("\n", "<br>");
                            subInfo += $"<br>{comment}";
                        }

                        // Add uploader info
                        if (subItem.Contributors.Count > 0)
                        {
                            subInfo += "<br>" + string.Join(", ", subItem.Contributors.ConvertAll(c => c.DisplayName));
                        }

                        var item = new SubtitleInfo
                        {
                            ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                            Id = link.GetId(),
                            ProviderName = Name,
                            Name = file.Name ?? "...",
                            PageLink = $"{ServerUrl}/subtitle/{subItem.SubtitleId}",
                            Format = file.GetExtSupportedByEmby(),
                            Author = subItem.Contributors.Count > 0 ? subItem.Contributors[0].DisplayName : "",
                            Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                            IsHearingImpaired = subItem.HearingImpaired,
                            Score = score,
                        };

                        res.Add(item);
                    }
                }
            }

            return res;
        }

        private async Task<ApiResponse<MovieSearchResponse>> SearchMovieCachedAsync(Dictionary<string, string> options, string apiKey, CancellationToken cancellationToken)
        {
            string cacheKey = "movie:" + string.Join(",", options);
            bool expiredFound = false;

            if (GetOptions().Cache.Search)
            {
                try
                {
                    using var stream = GetCacheSearch()!.Get(cacheKey);
                    return JsonSerializer.Deserialize<ApiResponse<MovieSearchResponse>>(stream) ?? throw new Exception("Cache deserialization return null!");
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
                    _logger.LogError(e, $"Unable to load movie search from cache: {e}");
                }
            }

            var resp = await SubSourceApi.SubSourceApi.SearchMoviesAsync(options, apiKey, cancellationToken).ConfigureAwait(false);

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
                    _logger.LogError(e, $"Unable to add movie search response to cache: {e}");
                }
            }
            else
            if (expiredFound)
            {
                using var stream = GetCacheSearch(-1)!.Get(cacheKey);
                return JsonSerializer.Deserialize<ApiResponse<MovieSearchResponse>>(stream) ?? throw new Exception("Cache deserialization return null!");
            }

            return resp;
        }

        private async Task<ApiResponse<SubtitleListResponse>> SearchSubtitlesCachedAsync(Dictionary<string, string> options, string apiKey, CancellationToken cancellationToken)
        {
            string cacheKey = "subtitle:" + string.Join(",", options);
            bool expiredFound = false;

            if (GetOptions().Cache.Search)
            {
                try
                {
                    using var stream = GetCacheSearch()!.Get(cacheKey);
                    return JsonSerializer.Deserialize<ApiResponse<SubtitleListResponse>>(stream) ?? throw new Exception("Cache deserialization return null!");
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

            var resp = await SubSourceApi.SubSourceApi.GetSubtitlesAsync(options, apiKey, cancellationToken).ConfigureAwait(false);

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
                return JsonSerializer.Deserialize<ApiResponse<SubtitleListResponse>>(stream) ?? throw new Exception("Cache deserialization return null!");
            }

            return resp;
        }

    }
}
