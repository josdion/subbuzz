using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Html.Parser;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Extensions;
using subbuzz.Helpers;

#if EMBY
using MediaBrowser.Common.Net;
using ILogger = MediaBrowser.Model.Logging.ILogger;
#else
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger<subbuzz.Providers.SubBuzz>;
#endif

#if JELLYFIN
using System.Net.Http;
#endif

namespace subbuzz.Providers
{
    class YifySubtitles : ISubBuzzProvider, IHasOrder
    {
        internal const string NAME = "YIFY Subtitles";
        private const string ServerUrl = "https://yifysubtitles.org";
        private const string HttpReferer = "https://yifysubtitles.org/";

        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;
        private Download downloader;
        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Movie };

        public int Order => 0;

        public YifySubtitles(
            ILogger logger,
            IFileSystem fileSystem,
            ILocalizationManager localizationManager,
            ILibraryManager libraryManager,
#if JELLYFIN
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
                return await downloader.GetArchiveSubFile(
                    id, 
                    HttpReferer, 
                    Encoding.GetEncoding(1251),
                    Plugin.Instance.Configuration.EncodeSubtitlesToUTF8,
                    cancellationToken).ConfigureAwait(false);
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
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var res = new List<SubtitleInfo>();

            try
            {
                if (!Plugin.Instance.Configuration.EnableYifySubtitles)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager);
                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                var tasks = new List<Task<List<SubtitleInfo>>>();

                if (!String.IsNullOrWhiteSpace(si.ImdbId))
                {
                    // search by IMDB Id
                    string urlImdb = String.Format($"{ServerUrl}/movie-imdb/{si.ImdbId}");
                    tasks.Add(SearchUrl(urlImdb, si, true, cancellationToken));
                }
                else
                {
                    // TODO: url = $"{ServerUrl}/search?q={HttpUtility.UrlEncode(si.SearchText)};
                    _logger.LogInformation($"{NAME}: IMDB ID missing");
                    return res;
                }

                foreach (var task in tasks)
                {
                    List<SubtitleInfo> subs = await task;
                    Utils.MergeSubtitleInfo(res, subs);
                }

                //res.Sort((x, y) => y.Score.CompareTo(x.Score));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Search error: {e}");
            }

            watch.Stop();
            _logger.LogInformation($"{NAME}: Search duration: {watch.ElapsedMilliseconds / 1000.0} sec. Subtitles found: {res.Count}");

            return res;
        }

        protected async Task<List<SubtitleInfo>> SearchUrl(string url, SearchInfo si, bool byImdb, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"{NAME}: GET: {url}");

                using (var html = await downloader.GetStream(url, HttpReferer, null, cancellationToken))
                {
                    return await ParseHtml(html, si, byImdb, cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: GET: {url}: Search error: {e}");
                return new List<SubtitleInfo>();
            }
        }

        protected async Task<List<SubtitleInfo>> ParseHtml(System.IO.Stream html, SearchInfo si, bool byImdb, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = new HtmlParser(context);
            var htmlDoc = parser.ParseDocument(html);

            var tagTitle = htmlDoc.GetElementsByClassName("movie-main-title").FirstOrDefault();
            if (tagTitle == null)
            {
                _logger.LogInformation($"{NAME}: Invalid HTML. Can't find element with class=movie-main-title");
                return res;
            }

            string subTitle = tagTitle.TextContent;

            var tbl = htmlDoc.QuerySelector("table.other-subs > tbody");
            var trs = tbl?.GetElementsByTagName("tr");
            if (trs == null)
            {
                _logger.LogInformation($"{NAME}: Invalid HTML");
                return res;
            }

            foreach (var tr in trs)
            {
                var tds = tr.GetElementsByTagName("td");
                if (tds == null || tds.Count() < 5) continue;

                string subRating = tds[0].TextContent;
                string subUploader = tds[4].TextContent;

                string lang = tds[1].TextContent;
                if (!lang.Equals(si.LanguageInfo.DisplayName, StringComparison.CurrentCultureIgnoreCase) &&
                    !lang.Equals(si.LanguageInfo.Name, StringComparison.CurrentCultureIgnoreCase))
                {
                    // Ignore language
                    continue;
                }

                var link = tds[2].GetElementsByTagName("a")[0];
                string subLinkPage = link.GetAttribute("href");
                var regexLink = new Regex(@"/subtitles/");
                string subLink;
                if (subLinkPage.Contains("://"))
                    subLink = regexLink.Replace(subLinkPage, "/subtitle/", 1) + ".zip";
                else
                    subLink = regexLink.Replace(subLinkPage, ServerUrl + "/subtitle/", 1) + ".zip";

                string subInfoBase = link.InnerHtml;
                var regexInfo = new Regex(@"<span.*/span>");
                subInfoBase = regexInfo.Replace(subInfoBase, "").Trim();
                string subInfo = subTitle + (String.IsNullOrWhiteSpace(subInfoBase) ? "" : "<br>" + subInfoBase);
                subInfo += string.Format("<br>{0}", subUploader);

                var files = await downloader.GetArchiveFileNames(subLink, HttpReferer, cancellationToken).ConfigureAwait(false);

                foreach (var (fileName, fileExt) in files) 
                {
                    if (fileExt != "srt" && fileExt != "sub") continue;

                    SubtitleScore subScore = new SubtitleScore();
                    if (byImdb) subScore.AddMatch("imdb");

                    float score = si.CaclScore(fileName, subScore, files.Count == 1 && subInfoBase.ContainsIgnoreCase(si.FileName));

                    var item = new SubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = Download.GetId(subLink, "", si.LanguageInfo.TwoLetterISOLanguageName, si.VideoFps.ToString()),
                        ProviderName = Name,
                        Name = $"<a href='{ServerUrl}{subLinkPage}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{fileName}</a>",
                        Format = fileExt,
                        Author = subUploader,
                        Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                        //DateCreated = DateTimeOffset.Parse(subDate),
                        CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                        //DownloadCount = int.Parse(subDownloads),
                        IsHashMatch = score >= Plugin.Instance.Configuration.HashMatchByScore,
                        IsForced = false,
                        Score = score,
                    };

                    res.Add(item);
                }
            }

            return res;
        }

    }
}
