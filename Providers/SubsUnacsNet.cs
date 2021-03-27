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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

#if EMBY
using subbuzz.Logging;
using ILogger = MediaBrowser.Model.Logging.ILogger;
#else
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger<subbuzz.Providers.SubsUnacsNet>;
#endif

#if JELLYFIN_10_7
using System.Net.Http;
#endif

namespace subbuzz.Providers
{
    public class SubsUnacsNet : ISubtitleProvider, IHasOrder
    {
        private const string NAME = "subsunacs.net";
        private const string ServerUrl = "https://subsunacs.net";
        private const string HttpReferer = "https://subsunacs.net/search.php";
        private readonly List<string> Languages = new List<string> { "bg", "en" };

        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;
        private Download downloader;

        private static Dictionary<string, string> InconsistentTvs = new Dictionary<string, string>
        {
            { "Marvel's Daredevil", "Daredevil" },
            { "Marvel's Luke Cage", "Luke Cage" },
            { "Marvel's Iron Fist", "Iron Fist" },
            { "DC's Legends of Tomorrow", "Legends of Tomorrow" },
            { "Doctor Who (2005)", "Doctor Who" },
            { "Star Trek: Deep Space Nine", "Star Trek DS9" },
            { "Star Trek: The Next Generation", "Star Trek TNG" },
        };

        private static Dictionary<string, string> InconsistentMovies = new Dictionary<string, string>
        {
            { "Back to the Future Part III", "Back to the Future 3" },
            { "Back to the Future Part II", "Back to the Future 2" },
            { "Bill & Ted Face the Music", "Bill Ted Face the Music" },
        };

        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

        public SubsUnacsNet(
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
                if (!Plugin.Instance.Configuration.EnableSubsunacsNet)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(
                    request,
                    _localizationManager,
                    _libraryManager,
                    "{0} {1:D2}x{2:D2}",
                    InconsistentTvs,
                    InconsistentMovies);

                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                if (!Languages.Contains(si.Lang) || String.IsNullOrEmpty(si.SearchText))
                {
                    return res;
                }

                var post_params = GetPostParams(
                    si.SearchText, 
                    si.Lang != "en" ? "0" : "1",
                    request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "");

                using (var html = await downloader.GetStream($"{ServerUrl}/search.php", HttpReferer, post_params, cancellationToken))
                {
                    var subs = await ParseHtml(html, si, cancellationToken);
                    res.AddRange(subs);
                }

                if (request.ContentType == VideoContentType.Episode && request.ParentIndexNumber == 0)
                {
                    // Search for special episodes by name
                    var post_params2 = GetPostParams(si.SearchEpByName, si.Lang != "en" ? "0" : "1", Convert.ToString(request.ProductionYear));
                    using (var html = await downloader.GetStream($"{ServerUrl}/search.php", HttpReferer, post_params2, cancellationToken))
                    {
                        var subs = await ParseHtml(html, si, cancellationToken);
                        res.AddRange(subs);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Search error: {e}");
            }

            return res;
        }

        protected Dictionary<string, string> GetPostParams(string m, string l, string y)
        {
            return new Dictionary<string, string>
                {
                    { "m", m }, // search text
                    { "l", l }, // language - 0: bulgarian, 1: english
                    { "c", "" }, // country
                    { "y", y }, // year
                    { "action", "   Търси   " },
                    { "a", "" }, // actor
                    { "d", "" }, // director
                    { "u", "" }, // uploader
                    { "g", "" }, // genre
                    { "t", "" },
                    { "imdbcheck", "1" }
                };
        }

        protected async Task<IEnumerable<RemoteSubtitleInfo>> ParseHtml(System.IO.Stream html, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<RemoteSubtitleInfo>();

            var config = AngleSharp.Configuration.Default;
            var context = AngleSharp.BrowsingContext.New(config);
            var parser = new AngleSharp.Html.Parser.HtmlParser(context);
            var htmlDoc = parser.ParseDocument(html);

            var trNodes = htmlDoc.QuerySelectorAll("tr[onmouseover]");
            foreach (var tr in trNodes)
            {
                var tdNodes = tr.GetElementsByTagName("td");
                if (tdNodes == null || tdNodes.Count() < 6) continue;

                var link = tdNodes[0].QuerySelector("a");
                if (link == null) continue;

                string subLink = ServerUrl + link.GetAttribute("href");
                string subTitle = link.InnerHtml;

                string subNotes = link.GetAttribute("title");

                var regex = new Regex(@"(?:.*<b>Дата: </b>)(.*)(?:<br><b>Инфо: </b><br>)(.*)(?:</div>)");
                string subDate = regex.Replace(subNotes, "$1");
                string subInfo = regex.Replace(subNotes, "$2");

                subInfo = Utils.TrimString(subInfo, "<br>");
                subInfo = subInfo.Replace("<br><br>", "<br>").Replace("<br><br>", "<br>");

                string subNumCd = tdNodes[1].InnerHtml;
                string subFps = tdNodes[2].InnerHtml;

                string subRating = "0";
                var rtImgNode = tdNodes[3].QuerySelector("img");
                if (rtImgNode != null) subRating = rtImgNode.GetAttribute("title");

                var linkUploader = tdNodes[5].QuerySelector("a");
                string subUploader = linkUploader == null ? "" : linkUploader.InnerHtml;
                string subDownloads = tdNodes[6].InnerHtml;

                subInfo += String.Format("<br>{0} | {1} | {2}", subDate, subUploader, subFps);

                var files = await downloader.GetArchiveSubFileNames(subLink, HttpReferer, cancellationToken).ConfigureAwait(false);
                foreach (var file in files)
                {
                    string fileExt = file.Split('.').LastOrDefault().ToLower();
                    if (fileExt == "txt" && Regex.IsMatch(file, @"subsunacs\.net|танете част|прочети|^read ?me|procheti", RegexOptions.IgnoreCase)) continue;
                    if (fileExt != "srt" && fileExt != "sub" && fileExt != "txt") continue;

                    var item = new RemoteSubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = Download.GetId(subLink, file, si.LanguageInfo.TwoLetterISOLanguageName, subFps),
                        ProviderName = Name,
                        Name = $"<a href='{subLink}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file}</a>",
                        Format = fileExt,
                        Author = subUploader,
                        Comment = subInfo,
                        //DateCreated = DateTimeOffset.Parse(subDate),
                        CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                        DownloadCount = int.Parse(subDownloads),
                        IsHashMatch = false,
#if EMBY
                        IsForced = false,
#endif
                    };

                    res.Add(item);
                }
            }

            return res;
        }

        protected async Task<IEnumerable<RemoteSubtitleInfo>> ParseHtmlAgilityPack(System.IO.Stream html, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<RemoteSubtitleInfo>();

            var htmlDoc = new HtmlDocument();
            htmlDoc.Load(html, Encoding.GetEncoding(1251), true);

            var trNodes = htmlDoc.DocumentNode.SelectNodes("//tr[@onmouseover]");
            if (trNodes == null) return res;

            for (int i = 0; i < trNodes.Count; i++)
            {
                var tdNodes = trNodes[i].SelectNodes(".//td");
                if (tdNodes == null || tdNodes.Count < 6) continue;

                HtmlNode linkNode = tdNodes[0].SelectSingleNode("a[@href]");
                if (linkNode == null) continue;

                string subLink = ServerUrl + linkNode.Attributes["href"].Value;
                string subTitle = linkNode.InnerText;

                string subNotes = linkNode.Attributes["title"].DeEntitizeValue;

                var regex = new Regex(@"(?:.*<b>Дата: </b>)(.*)(?:<br><b>Инфо: </b><br>)(.*)(?:</div>)");
                string subDate = regex.Replace(subNotes, "$1");
                string subInfo = regex.Replace(subNotes, "$2");

                subInfo = Utils.TrimString(subInfo, "<br>");
                subInfo = subInfo.Replace("<br><br>", "<br>").Replace("<br><br>", "<br>");

                string subNumCd = tdNodes[1].InnerText;
                string subFps = tdNodes[2].InnerText;

                string subRating = "0";
                var rtImgNode = tdNodes[3].SelectSingleNode(".//img");
                if (rtImgNode != null) subRating = rtImgNode.Attributes["title"].Value;

                string subUploader = tdNodes[5].InnerText;
                string subDownloads = tdNodes[6].InnerText;

                subInfo += String.Format("<br>{0} | {1} | {2}", subDate, subUploader, subFps);

                var files = await downloader.GetArchiveSubFileNames(subLink, HttpReferer, cancellationToken).ConfigureAwait(false);
                foreach (var file in files)
                {
                    string fileExt = file.Split('.').LastOrDefault().ToLower();
                    if (fileExt != "srt" && fileExt != "sub") continue;

                    var item = new RemoteSubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = Download.GetId(subLink, file, si.LanguageInfo.TwoLetterISOLanguageName, subFps),
                        ProviderName = Name,
                        Name = $"<a href='{subLink}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file}</a>",
                        Format = fileExt,
                        Author = subUploader,
                        Comment = subInfo,
                        //DateCreated = DateTimeOffset.Parse(subDate),
                        CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                        DownloadCount = int.Parse(subDownloads),
                        IsHashMatch = false,
#if EMBY
                        IsForced = false,
#endif
                    };

                    res.Add(item);
                }
            }

            return res;
        }

    }
}
