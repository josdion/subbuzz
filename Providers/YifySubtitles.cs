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
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Extensions;
using subbuzz.Helpers;

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

            string subTitle = htmlDoc.GetElementsByClassName("movie-main-title").FirstOrDefault().TextContent;

            var tbl = htmlDoc.QuerySelector("table.other-subs > tbody");
            var trs = tbl?.GetElementsByTagName("tr");
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
                var regexLink = new Regex(@"^/subtitles/");
                string subLink = regexLink.Replace(subLinkPage, ServerUrl + "/subtitle/") + ".zip";

                string subInfo = link.InnerHtml;
                var regexInfo = new Regex(@"<span.*/span>");
                subInfo = regexInfo.Replace(subInfo, "").Trim();
                subInfo = subTitle + (String.IsNullOrWhiteSpace(subInfo) ? "" : "<br>" + subInfo);
                subInfo += String.Format("<br>{0}", subUploader);

                var files = await downloader.GetArchiveSubFiles(subLink, HttpReferer, cancellationToken).ConfigureAwait(false);

                foreach (var fitem in files)
                {
                    string file = fitem.Name;
                    string fileExt = file.Split('.').LastOrDefault().ToLower();
                    if (fileExt != "srt" && fileExt != "sub") continue;

                    SubtitleScore subScore = new SubtitleScore();
                    if (byImdb) subScore.AddMatch("imdb");

                    Parser.MovieInfo mvInfo = Parser.Movie.ParseTitle(file, true);
                    si.CheckMovie(mvInfo, ref subScore, true);
                    float score = subScore.CalcScoreMovie();

                    var item = new SubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = Download.GetId(subLink, "", si.LanguageInfo.TwoLetterISOLanguageName, si.VideoFps.ToString()),
                        ProviderName = Name,
                        Name = $"<a href='{ServerUrl}{subLinkPage}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file}</a>",
                        Format = "SRT",
                        Author = subUploader,
                        Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                        //DateCreated = DateTimeOffset.Parse(subDate),
                        CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                        //DownloadCount = int.Parse(subDownloads),
                        IsHashMatch = false,
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
