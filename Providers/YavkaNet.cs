using HtmlAgilityPack;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace subbuzz.Providers
{
    public class YavkaNet : ISubtitleProvider, IHasOrder
    {
        private const string HttpReferer = "http://yavka.net/subtitles.php";
        private readonly List<string> Languages = new List<string> { "bg", "en", "ru", "es", "it" };

        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;

        public string Name => $"[{Plugin.NAME}] <b>yavka.net</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

        public YavkaNet(ILogger logger, IFileSystem fileSystem, IHttpClient httpClient,
            ILocalizationManager localizationManager, ILibraryManager libraryManager)
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _httpClient = httpClient;
            _localizationManager = localizationManager;
            _libraryManager = libraryManager;
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            try
            {
                return await Download.ArchiveSubFile(_httpClient, id, HttpReferer).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.ErrorException(Name + ":GetSubtitles:Exception:", e);
            }

            return new SubtitleResponse();
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            var res = new List<RemoteSubtitleInfo>();

            var languageInfo = _localizationManager.FindLanguageInfo(request.Language.AsSpan());
            var lang = languageInfo.TwoLetterISOLanguageName.ToLower();

            if (!Languages.Contains(lang))
            {
                return res;
            }

            try
            {
                BaseItem libItem = _libraryManager.FindByPath(request.MediaPath, false);
                if (libItem == null)
                {
                    _logger.Info($"{Name} No library info for {request.MediaPath}");
                    return res;
                }

                string searchText = "";

                if (request.ContentType == VideoContentType.Movie)
                {
                    searchText = libItem.OriginalTitle;
                }
                else
                if (request.ContentType == VideoContentType.Episode)
                {
                    Episode ep = libItem as Episode;
                    searchText = String.Format("{0} s{1:D2}e{2:D2}", ep.Series.OriginalTitle, request.ParentIndexNumber ?? 0, request.IndexNumber ?? 0);
                }
                else
                {
                    return res;
                }

                _logger?.Info($"{Name} Request subtitle for '{searchText}', language={lang}, year={request.ProductionYear}");

                var opts = new HttpRequestOptions
                {
                    Url = String.Format(
                        "http://yavka.net/subtitles.php?s={0}&y={1}&c=&u=&l={2}&g=&i=",
                        HttpUtility.UrlEncode(searchText),
                        request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "",
                        lang.ToUpper()
                        ),

                    UserAgent = Download.UserAgent,
                    Referer = HttpReferer,
                    TimeoutMs = 10000, //10 seconds timeout
                    EnableKeepAlive = false,
                    DecompressionMethod = CompressionMethod.Gzip,
                };

                using (var response = await _httpClient.GetResponse(opts).ConfigureAwait(false))
                {
                    using (var reader = new StreamReader(response.Content, System.Text.Encoding.UTF8))
                    {
                        var html = await reader.ReadToEndAsync().ConfigureAwait(false);

                        var htmlDoc = new HtmlDocument();
                        htmlDoc.LoadHtml(html);

                        var trNodes = htmlDoc.DocumentNode.SelectNodes("//tr");
                        if (trNodes == null) return res;

                        for (int i = 0; i < trNodes.Count; i++)
                        {

                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.ErrorException(Name + ":Search:Exception:", e);
            }

            return res;
        }
    }
}
