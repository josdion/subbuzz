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
using System.Security.Authentication;
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
            return new SubtitleResponse();
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request,
            CancellationToken cancellationToken)
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

                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}, IMDB={si.ImdbId}");

                var options = new Dictionary<string, string>
                {
                    { "languages", si.Lang },
                    { "type", request.ContentType == VideoContentType.Episode ? "episode" : "movie" },
                    { "query", si.SearchText }
                };

                string hash = "";// CalcHash(request.MediaPath);
                if (hash.IsNotNullOrWhiteSpace())
                {
                    options.Add("moviehash", hash);
                }

                if (si.ImdbId.IsNotNullOrWhiteSpace()) 
                    options.Add("imdb_id", si.ImdbId);

                if (request.ContentType == VideoContentType.Episode)
                {
                    options.Add("season_number", si.SeasonNumber?.ToString() ?? string.Empty);
                    options.Add("episode_number", si.EpisodeNumber?.ToString() ?? string.Empty);
                }

                var searchResponse = await OpenSubtitlesAPI.OpenSubtitles.SearchSubtitlesAsync(
                    options, apiKey, cancellationToken).ConfigureAwait(false);

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
                    subInfo += String.Format("<br>{0} | {1}", subItem.UploadDate, subItem.Uploader.Name);
                    if ((subItem.Fps ?? 0.0) > 0) subInfo += $" | {subItem.Fps}";

                    SubtitleScore subScoreBase = new SubtitleScore();
                    if (request.ContentType == VideoContentType.Episode)
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
                        si.CheckImdbId(subItem.FeatureDetails.ImdbId, ref subScoreBase);
                        si.CheckFps(subItem.Fps, ref subScoreBase);
                    }

                    foreach (var file in subItem.Files)
                    {
                        float score = si.CaclScore(file.FileName, subScoreBase, false);

                        var item = new SubtitleInfo
                        {
                            ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                            Id = $"{file.FileId}",
                            ProviderName = Name,
                            Name = $"<a href='{subItem.Url}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file.FileName}</a>",
                            Format = subItem.Format ?? "srt",
                            Author = subItem.Uploader.Name,
                            Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                            DateCreated = subItem.UploadDate,
                            //CommunityRating = Convert.ToInt32(subRating),
                            DownloadCount = subItem.DownloadCount,
                            //IsHashMatch = score >= Plugin.Instance.Configuration.HashMatchByScore,
                            IsForced = false,
                            Score = score,
                        };

                        res.Add(item);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Search error: {e}");
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

        private string CalcHash(string path)
        {
            string hash;
            try
            {
                using (var fileStream = System.IO.File.OpenRead(path))
                {
                    hash = RequestHelper.ComputeHash(fileStream);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Exception while computing hash for {path}");
            }

            return string.Empty;
        }
    }
}
