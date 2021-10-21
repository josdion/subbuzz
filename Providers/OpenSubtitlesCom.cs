using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Extensions;
using subbuzz.Helpers;
using subbuzz.Providers.OpenSubtitlesAPI;
using subbuzz.Providers.OpenSubtitlesAPI.Models.Responses;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if EMBY
using ILogger = MediaBrowser.Model.Logging.ILogger;
#else
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger<subbuzz.Providers.SubBuzz>;
#endif

#if JELLYFIN_10_7
using System.Net.Http;
#endif

namespace subbuzz.Providers
{
    class OpenSubtitlesCom : ISubBuzzProvider
    {
        internal const string NAME = "opensubtitles.com";
        private const string ServerUrl = "https://www.opensubtitles.com";

        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;

        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        private PluginConfiguration GetOptions()
            => Plugin.Instance.Configuration;

        public OpenSubtitlesCom(
            ILogger logger,
            IFileSystem fileSystem,
            ILocalizationManager localizationManager,
            ILibraryManager libraryManager,
#if JELLYFIN_10_7
            IHttpClientFactory http
#else
            IHttpClient http
#endif
            )
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _localizationManager = localizationManager;
            _libraryManager = libraryManager;

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            RequestHelper.Instance = new RequestHelper(http, version);
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            try
            {
                var apiKey = GetOptions().OpenSubApiKey;
                var token = GetOptions().OpenSubToken;

                var (fileId, lang, format, fps) = ParseId(id);
                var link = await GetDownloadLink(fileId, cancellationToken).ConfigureAwait(false);

                if (link.IsNullOrWhiteSpace())
                {
                    return new SubtitleResponse();
                }

                var res = await OpenSubtitles.DownloadSubtitleAsync(link, cancellationToken).ConfigureAwait(false);

                if (!res.Ok)
                {
                    _logger.LogInformation($"{NAME}: Subtitle with Id {id} could not be downloaded: {res.Code}");
                    return new SubtitleResponse();
                }

                var fileStream = SubtitleConvert.ToSupportedFormat(res.Data, Encoding.UTF8, GetOptions().EncodeSubtitlesToUTF8, fps, ref format);

                return new SubtitleResponse
                {
                    Format = format,
                    Language = lang,
                    IsForced = false,
                    Stream = fileStream,
                };
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: GetSubtitles error: {e}");
            }

            return new SubtitleResponse();
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            try
            {
                var apiKey = GetOptions().OpenSubApiKey;
                if (!GetOptions().EnableOpenSubtitles || apiKey.IsNullOrWhiteSpace())
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, "{0}");

                if (si.Lang == "zh") si.Lang = "zh-CN";
                else if (si.Lang == "pt") si.Lang = "pt-PT";

                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}, IMDB={si.ImdbId}");

                string hash = GetOptions().OpenSubUseHash ? CalcHash(request.MediaPath) : string.Empty;

                return await Search(si, apiKey, hash, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Search error: {e}");
            }

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
                if (si.ImdbIdInt > 0)
                {
                    options.Add("parent_imdb_id", si.ImdbIdInt.ToString());

                    if (si.ImdbIdEpisodeInt > 0)
                        options.Add("imdb_id", si.ImdbIdEpisodeInt.ToString());
                }
                else
                if (si.TmdbId.IsNotNullOrWhiteSpace())
                {
                    options.Add("parent_tmdb_id", si.TmdbId);

                    if (si.TmdbIdEpisode.IsNotNullOrWhiteSpace())
                        options.Add("tmdb_id", si.TmdbIdEpisode);
                }
                else
                {
                    options.Add("query", si.TitleSeries.ToLower());
                }

                options.Add("season_number", si.SeasonNumber?.ToString() ?? string.Empty);
                options.Add("episode_number", si.EpisodeNumber?.ToString() ?? string.Empty);
            }
            else
            {
                if (si.ImdbIdInt > 0)
                    options.Add("imdb_id", si.ImdbIdInt.ToString());
                else
                if (si.TmdbId.IsNotNullOrWhiteSpace())
                    options.Add("tmdb_id", si.TmdbId);
                else
                {
                    options.Add("query", si.TitleMovie.ToLower());
                    if ((si.Year ?? 0) > 0)
                    {
                        options["query"] += $" ({si.Year ?? 0})";
                        options.Add("year", $"{ si.Year ?? 0}");
                    }
                }
            }

            _logger.LogDebug("{NAME}: Search options: {options}", NAME, options);

            var searchResponse = await OpenSubtitles.SearchSubtitlesAsync(options, apiKey, cancellationToken).ConfigureAwait(false);

            if (!searchResponse.Ok)
            {
                _logger.LogInformation("{NAME}: Invalid response: {Code} - {Body}", NAME, searchResponse.Code, searchResponse.Body);
                return res;
            }

            foreach (ResponseData resItem in searchResponse.Data)
            {
                var subItem = resItem.Attributes;

                string itemTitle = $"{subItem.FeatureDetails.Title ?? subItem.FeatureDetails.ParentTitle} ({subItem.FeatureDetails.Year})";
                string subInfo = $"{itemTitle}<br>{subItem.Release}";
                subInfo += (subItem.Comments.IsNotNullOrWhiteSpace()) ? $"<br>{subItem.Comments}" : "";
                subInfo += String.Format("<br>{0} | {1}", subItem.UploadDate, subItem.Uploader.Name);
                if ((subItem.Fps ?? 0.0) > 0) subInfo += $" | {subItem.Fps}";

                SubtitleScore subScoreBase = new SubtitleScore();
                if (si.VideoType == VideoContentType.Episode)
                {
                    Parser.EpisodeInfo epInfoBase = Parser.Episode.ParseTitle(itemTitle);
                    si.CheckEpisode(epInfoBase, ref subScoreBase);
                    si.CheckImdbId(subItem.FeatureDetails.ImdbId, ref subScoreBase);
                    si.CheckImdbId(subItem.FeatureDetails.ParentImdbId, ref subScoreBase);
                    si.CheckFps(subItem.Fps, ref subScoreBase);
                }
                else
                {
                    Parser.MovieInfo mvInfoBase = Parser.Movie.ParseTitle(itemTitle);
                    si.CheckMovie(mvInfoBase, ref subScoreBase);

                    if (subItem.Release.IsNotNullOrWhiteSpace())
                    {
                        Parser.MovieInfo mvInfoBaseRel = Parser.Movie.ParseTitle(subItem.Release);
                        si.CheckMovie(mvInfoBaseRel, ref subScoreBase);
                    }

                    si.CheckImdbId(subItem.FeatureDetails.ImdbId, ref subScoreBase);
                    si.CheckFps(subItem.Fps, ref subScoreBase);
                }

                foreach (var file in subItem.Files)
                {
                    float score = si.CaclScore(file.FileName, subScoreBase, false);

                    var fileExt = string.Empty;
                    if (file.FileName != null)
                    {
                        fileExt = file.FileName.Split('.').LastOrDefault().ToLower();
                    }

                    var format = subItem.Format ?? (fileExt.IsNullOrWhiteSpace() ? "srt" : fileExt);

                    var item = new SubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = GetId(file.FileId, si.Lang, format, subItem.Fps ?? 25),
                        ProviderName = Name,
                        Name = $"<a href='{subItem.Url}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file.FileName ?? "..."}</a>",
                        Format = format,
                        Author = subItem.Uploader.Name,
                        Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                        DateCreated = subItem.UploadDate,
                        //CommunityRating = Convert.ToInt32(subRating),
                        DownloadCount = subItem.DownloadCount,
                        IsHashMatch = (score >= Plugin.Instance.Configuration.HashMatchByScore) || (subItem.MovieHashMatch ?? false),
                        IsForced = false,
                        Score = score,
                    };

                    res.Add(item);
                }
            }

            return res;
        }

        private async Task Login(CancellationToken cancellationToken)
        {
            var apiKey = GetOptions().OpenSubApiKey;
            var userName = GetOptions().OpenSubUserName;
            var password = GetOptions().OpenSubPassword;

            if (apiKey.IsNullOrWhiteSpace())
            {
                throw new AuthenticationException("API key is not set up");
            }

            if (userName.IsNullOrWhiteSpace() || password.IsNullOrWhiteSpace())
            {
                throw new AuthenticationException("Account username and/or password are not set up");
            }

            var loginResponse = await OpenSubtitlesAPI.OpenSubtitles.LogInAsync(
                userName, password, apiKey, cancellationToken).ConfigureAwait(false);

            if (!loginResponse.Ok)
                GetOptions().OpenSubToken = string.Empty;
            else
                GetOptions().OpenSubToken = loginResponse.Data?.Token;

            Plugin.Instance.SaveConfiguration();

            if (GetOptions().OpenSubToken.IsNullOrWhiteSpace())
            {
                _logger.LogInformation("Login failed: {Code} - {Body}", loginResponse.Code, loginResponse.Body);
                throw new AuthenticationException("Authentication to OpenSubtitles.com failed.");
            }
        }

        private async Task<string> GetDownloadLink(string fileId, CancellationToken cancellationToken)
        {
            var apiKey = GetOptions().OpenSubApiKey;
            var token = GetOptions().OpenSubToken;

            if (apiKey.IsNullOrWhiteSpace())
            {
                throw new AuthenticationException("API key is not set up");
            }

            if (token.IsNullOrWhiteSpace())
            {
                await Login(cancellationToken).ConfigureAwait(false);
                token = GetOptions().OpenSubToken;
            }

            var fid = int.Parse(fileId);
            var link = await OpenSubtitles.GetSubtitleLinkAsync(fid, token, apiKey, cancellationToken).ConfigureAwait(false);

            if (link.Ok)
            {
                return link.Data.Link;
            }
            else
            {
                switch (link.Code)
                {
                    case HttpStatusCode.NotAcceptable:
                        _logger.LogInformation($"{NAME}: OpenSubtitles.com download limit reached.");
                        break;

                    case HttpStatusCode.Unauthorized:
                        _logger.LogInformation($"{NAME}: JWT token expired, obtain a new one and try again");

                        GetOptions().OpenSubToken = string.Empty;
                        Plugin.Instance.SaveConfiguration();
                        return await GetDownloadLink(fileId, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation($"{NAME}: Invalid response for file {fid}: {link.Code}\n\n{link.Body}");
            }

            return string.Empty;
        }

        private string CalcHash(string path)
        {
            try
            {
                using (var fileStream = System.IO.File.OpenRead(path))
                {
                    return RequestHelper.ComputeHash(fileStream);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Exception while computing hash for {path}");
            }

            return string.Empty;
        }

        private const string UrlSeparator = "*:*";

        private static string GetId(int fileId, string lang, string format, float fps)
        {
            return Utils.Base64UrlEncode($"{fileId}{UrlSeparator}{lang}{UrlSeparator}{format}{UrlSeparator}{fps}");
        }

        private static (string, string, string, float) ParseId(string id)
        {
            string[] ids = Utils.Base64UrlDecode(id).Split(new[] { UrlSeparator }, StringSplitOptions.None);
            string fileId = ids[0];
            string lang = ids[1];
            string format = ids[2];

            float fps = 25;
            try { fps = float.Parse(ids[3].Replace(',', '.'), CultureInfo.InvariantCulture); } catch { }

            return (fileId, lang, format, fps);
        }

    }
}
