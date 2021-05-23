using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Extensions;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Text;
using System.Globalization;

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
    class YavkaNet : ISubBuzzProvider, IHasOrder
    {
        internal const string NAME = "yavka.net";
        private const string ServerUrl = "https://yavka.net";
        private const string HttpReferer = "https://yavka.net/subtitles.php";
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
            var res = new List<SubtitleInfo>();

            try
            {
                if (!Plugin.Instance.Configuration.EnableYavkaNet)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, "{0} s{1:D2}e{2:D2}", "{0} s{1:D2}");
                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                if (!Languages.Contains(si.Lang) || String.IsNullOrEmpty(si.SearchText))
                {
                    return res;
                }

                var tasks = new List<Task<List<SubtitleInfo>>>();

                if (!String.IsNullOrWhiteSpace(si.ImdbId))
                {
                    // search by IMDB Id
                    string urlImdb = String.Format(
                        "{0}/subtitles.php?s={1}&y=&c=&u=&l={2}&g=&i={3}",
                        ServerUrl,
                        request.ContentType == VideoContentType.Episode ?
                            String.Format("s{0:D2}e{1:D2}", request.ParentIndexNumber ?? 0, request.IndexNumber ?? 0) : "",
                        si.Lang.ToUpper(),
                        si.ImdbId
                        );

                    tasks.Add(SearchUrl(urlImdb, si, cancellationToken));
                }

                if (!String.IsNullOrWhiteSpace(si.SearchText))
                {
                    // search for movies/series by title
                    string url = String.Format(
                            "{0}/subtitles.php?s={1}&y={2}&c=&u=&l={3}&g=&i=",
                            ServerUrl,
                            HttpUtility.UrlEncode(si.SearchText),
                            request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "",
                            si.Lang.ToUpper()
                            );

                    tasks.Add(SearchUrl(url, si, cancellationToken));
                }

                if (request.ContentType == VideoContentType.Episode && !String.IsNullOrWhiteSpace(si.SearchSeason) && (si.SeasonNumber ?? 0) > 0)
                {
                    // search for episodes in season packs
                    string urlSeason = String.Format(
                            "{0}/subtitles.php?s={1}&y={2}&c=&u=&l={3}&g=&i=",
                            ServerUrl,
                            HttpUtility.UrlEncode(si.SearchSeason),
                            "",
                            si.Lang.ToUpper()
                            );

                    tasks.Add(SearchUrl(urlSeason, si, cancellationToken));
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

        protected async Task<List<SubtitleInfo>> SearchUrl(string url, SearchInfo si, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"{NAME}: GET: {url}");

                using (var html = await downloader.GetStream(url, HttpReferer, null, cancellationToken))
                {
                    return await ParseHtml(html, si, cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: GET: {url}: Search error: {e}");
                return new List<SubtitleInfo>();
            }
        }

        protected async Task<List<SubtitleInfo>> ParseHtml(System.IO.Stream html, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            var config = AngleSharp.Configuration.Default;
            var context = AngleSharp.BrowsingContext.New(config);
            var parser = new AngleSharp.Html.Parser.HtmlParser(context);
            var htmlDoc = parser.ParseDocument(html);

            var trNodes = htmlDoc.GetElementsByTagName("tr");
            foreach (var tr in trNodes)
            {
                var tds = tr.GetElementsByTagName("td");
                if (tds == null || tds.Count() < 1) continue;
                var td = tds[0];

                var link = td.QuerySelector("a[class='balon']");
                if (link == null)
                {
                    link = td.QuerySelector("a[class='selector']");
                    if (link == null) continue;
                }

                string subLink = ServerUrl + "/" + link.GetAttribute("href").Trim('/') + "/";
                string subTitle = link.InnerHtml;

                string subYear = "";
                if (link.NextSibling != null)
                    subYear = link.NextElementSibling.TextContent.Trim(new[] { ' ', '(', ')' });

                if (!String.IsNullOrWhiteSpace(subYear))
                    subTitle += $" ({subYear})";

                SubtitleScore subScoreBase = new SubtitleScore();
                Parser.EpisodeInfo epInfoBase = Parser.Episode.ParseTitle(subTitle);
                Parser.MovieInfo mvInfoBase = Parser.Movie.ParseTitle(subTitle);
                si.CheckEpisode(epInfoBase, ref subScoreBase);
                si.CheckMovie(mvInfoBase, ref subScoreBase);

                string subNotes = link.GetAttribute("content");
                var regex = new Regex(@"(?s)<p.*><img [A-z0-9=\'/\. :;#-]*>(.*)</p>");
                string subInfoBase = regex.Replace(subNotes, "$1");

                string subInfo = Utils.TrimString(subInfoBase, "<br />");
                subInfo = subInfo.Replace("<br /><br />", "<br />").Replace("<br /><br />", "<br />");
                subInfo = subTitle + (String.IsNullOrWhiteSpace(subInfo) ? "" : "<br>" + subInfo);

                string subFps = "";
                var fps = td.QuerySelector("span[title='Кадри в секунда']");
                if (fps != null)
                    subFps = fps.TextContent.Trim();

                string subUploader = "";
                var upl = td.QuerySelector("a[class='click']");
                if (upl != null)
                    subUploader = upl.TextContent.Trim();

                string subDownloads = "0";
                var downlds = td.QuerySelector("div > strong");
                if (downlds != null)
                    subDownloads = downlds.TextContent.Trim();

                var subFiles = new List<ArchiveFileInfo>();
                var files = await downloader.GetArchiveSubFiles(subLink, HttpReferer, cancellationToken).ConfigureAwait(false);

                int imdbId = 0;
                string subImdb = "";
                DateTime? dt = null;
                DateTimeOffset? dtOffset = null;

                foreach (var fitem in files)
                {
                    if (fitem.Name == "YavkA.net.txt")
                    {
                        fitem.Content.Seek(0, System.IO.SeekOrigin.Begin);
                        var reader = new System.IO.StreamReader(fitem.Content, Encoding.UTF8, true);
                        string info_text = reader.ReadToEnd();
                        var regexDate = new Regex(@"Качени на: (\d\d\d\d-\d\d-\d\d \d\d:\d\d:\d\d)");
                        var match = regexDate.Match(info_text);
                        if (match.Success && match.Groups.Count > 0)
                        {
                            dt = DateTime.Parse(match.Groups[1].ToString(), System.Globalization.CultureInfo.InvariantCulture);
                            dtOffset = DateTimeOffset.Parse(match.Groups[1].ToString(), System.Globalization.CultureInfo.InvariantCulture);
                        }

                        var regexImdbId = new Regex(@"iMDB ID: (tt(\d+))");
                        match = regexImdbId.Match(info_text);
                        if (match.Success && match.Groups.Count > 2)
                        {
                            subImdb = match.Groups[1].ToString();
                            imdbId = int.Parse(match.Groups[2].ToString());
                        }
                    }
                    else
                    {
                        string fileExt = fitem.Ext.ToLower();
                        if (fileExt != "srt" && fileExt != "sub") continue;

                        subFiles.Add(fitem);
                    }
                }

                if (!si.CheckImdbId(imdbId, ref subScoreBase))
                {
                    //_logger.LogInformation($"{NAME}: Ignore result {subImdb} {subTitle} not matching IMDB ID");
                    //continue;
                }

                si.CheckFps(subFps, ref subScoreBase);

                string subDate = dtOffset != null ? dtOffset?.ToString("g") : "";
                subInfo += String.Format("<br>{0} | {1} | {2}", subDate, subUploader, subFps);

                foreach (var fitem in subFiles)
                {
                    string file = fitem.Name;
                    float score = si.CaclScore(file, subScoreBase, subFiles.Count == 1 && subInfoBase.ContainsIgnoreCase(si.FileName));

                    if (score == 0 || score < Plugin.Instance.Configuration.MinScore)
                        continue;

                    var item = new SubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = Download.GetId(subLink, file, si.LanguageInfo.TwoLetterISOLanguageName, ""),
                        ProviderName = Name,
                        Name = $"<a href='{subLink}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file}</a>",
                        Format = fitem.Ext,
                        Author = subUploader,
                        Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                        //CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                        DownloadCount = int.Parse(subDownloads),
                        IsHashMatch = score >= Plugin.Instance.Configuration.HashMatchByScore,
                        IsForced = false,
                        Score = score,
#if EMBY
                        DateCreated = dtOffset,
#else
                        DateCreated = dt,
#endif
                    };

                    res.Add(item);
                }

            }

            return res;
        }

    }
}
