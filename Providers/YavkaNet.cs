using HtmlAgilityPack;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Text;

#if EMBY
using subbuzz.Logging;
using ILogger = MediaBrowser.Model.Logging.ILogger;
#else
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger<subbuzz.Providers.YavkaNet>;
#endif

#if JELLYFIN_10_7
using System.Net.Http;
#endif

namespace subbuzz.Providers
{
    public class YavkaNet : ISubtitleProvider, IHasOrder
    {
        private const string NAME = "yavka.net";
        private const string HttpReferer = "http://yavka.net/subtitles.php";
        private readonly List<string> Languages = new List<string> { "bg", "en", "ru", "es", "it" };

        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;
        private Download downloader;
        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

        public YavkaNet(
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
            downloader = new Download(http);
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            try
            {
                return await downloader.GetArchiveSubFile(id, HttpReferer, Encoding.GetEncoding(1251), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: GetSubtitles error: {e}");
            }

            return new SubtitleResponse();
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            var res = new List<RemoteSubtitleInfo>();

            try
            {
                if (!Plugin.Instance.Configuration.EnableYavkaNet)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, "{0} s{1:D2}e{2:D2}");
                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                if (!Languages.Contains(si.Lang) || String.IsNullOrEmpty(si.SearchText))
                {
                    return res;
                }

                string url = String.Format(
                        "http://yavka.net/subtitles.php?s={0}&y={1}&c=&u=&l={2}&g=&i=",
                        HttpUtility.UrlEncode(si.SearchText),
                        request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "",
                        si.Lang.ToUpper()
                        );

                if (!String.IsNullOrWhiteSpace(si.ImdbId))
                {
                    // search by IMDB Id
                    url = String.Format(
                        "http://yavka.net/subtitles.php?s={0}&y=&c=&u=&l={1}&g=&i={2}",
                        request.ContentType == VideoContentType.Episode ? 
                            String.Format("s{0:D2}e{1:D2}", request.ParentIndexNumber ?? 0, request.IndexNumber ?? 0) : "",
                        si.Lang.ToUpper(),
                        si.ImdbId
                        );
                }

                _logger.LogInformation($"{url}");

                using (var html = await downloader.GetStream(url, HttpReferer, null, cancellationToken))
                {
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.Load(html, Encoding.UTF8, true);

                    var trNodes = htmlDoc.DocumentNode.SelectNodes("//tr");
                    if (trNodes == null) return res;

                    for (int i = 0; i < trNodes.Count; i++)
                    {
                        var tdNodes = trNodes[i].SelectNodes(".//td");
                        if (tdNodes == null) continue;

                        HtmlNode linkNode = tdNodes[0].SelectSingleNode("a[@class='balon' or @class='selector']");
                        if (linkNode == null) continue;

                        string subLink = "http://yavka.net/" + linkNode.Attributes["href"].Value;
                        string subTitle = linkNode.InnerText;

                        string subNotes = linkNode.Attributes["content"].DeEntitizeValue;
                        var regex = new Regex(@"(?s)<p.*><img [A-z0-9=\'/\. :;#]*>(.*)</p>");
                        string subInfo = regex.Replace(subNotes, "$1");

                        //string subYear = tdNodes[0].SelectSingleNode(".//span").InnerText.Trim(new[] { ' ', '(', ')' });
                        //string subFps =  trNodes[i].SelectSingleNode(".//span[@title='Кадри в секунда']").InnerText;

                        string subDownloads = "0";
                        var dnldNode = trNodes[i].SelectSingleNode(".//div//strong");
                        if (dnldNode != null) subDownloads = dnldNode.InnerText;

                        //var upl = trNodes[i].SelectSingleNode(".//a[@class='click']");

                        var files = await downloader.GetArchiveSubFileNames(subLink, HttpReferer, cancellationToken).ConfigureAwait(false);
                        foreach (var file in files)
                        {
                            string fileExt = file.Split('.').LastOrDefault().ToLower();
                            if (fileExt != "srt" && fileExt != "sub") continue;

                            var item = new RemoteSubtitleInfo
                            {
                                ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                                Id = Download.GetId(subLink, file, si.LanguageInfo.TwoLetterISOLanguageName, ""),
                                ProviderName = Name,
                                Name = file,
                                Format = fileExt,
                                //Author = subUploader,
                                Comment = subInfo,
                                //DateCreated = DateTimeOffset.Parse(subDate),
                                //CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                                DownloadCount = int.Parse(subDownloads),
                                IsHashMatch = false,
#if EMBY
                                IsForced = false,
#endif
                            };

                            res.Add(item);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Search error: {e}");
            }

            return res;
        }
    }
}
