﻿using MediaBrowser.Common.Net;
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
            var res = new List<RemoteSubtitleInfo>();

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

                var tasks = new List<Task<List<RemoteSubtitleInfo>>>();

                /* searchin by IMDB Id is not working at the moment
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
                }*/

                if (!String.IsNullOrWhiteSpace(si.SearchText))
                {
                    // search by movies/series title

                    string url = String.Format(
                            "{0}/subtitles.php?s={1}&y={2}&c=&u=&l={3}&g=&i=",
                            ServerUrl,
                            HttpUtility.UrlEncode(si.SearchText),
                            request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "",
                            si.Lang.ToUpper()
                            );

                    tasks.Add(SearchUrl(url, si, cancellationToken));
                }

                if (request.ContentType == VideoContentType.Episode && !String.IsNullOrWhiteSpace(si.SearchSeason))
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
                    List<RemoteSubtitleInfo> subs = await task;
                    Utils.MergeSubtitleInfo(res, subs);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Search error: {e}");
            }

            return res;
        }

        protected async Task<List<RemoteSubtitleInfo>> SearchUrl(string url, SearchInfo si, CancellationToken cancellationToken)
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
                return new List<RemoteSubtitleInfo>();
            }
        }

        protected async Task<List<RemoteSubtitleInfo>> ParseHtml(System.IO.Stream html, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<RemoteSubtitleInfo>();

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

                string subNotes = link.GetAttribute("content");
                var regex = new Regex(@"(?s)<p.*><img [A-z0-9=\'/\. :;#-]*>(.*)</p>");
                string subInfo = regex.Replace(subNotes, "$1");

                subInfo = Utils.TrimString(subInfo, "<br />");
                subInfo = subInfo.Replace("<br /><br />", "<br />").Replace("<br /><br />", "<br />");

                string subYear = "";
                if (link.NextSibling != null)
                    subYear = link.NextElementSibling.TextContent.Trim(new[] { ' ', '(', ')' });

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

                        break;
                    }
                }

                if (!si.CheckImdbId(imdbId))
                {
                    _logger.LogInformation($"{NAME}: Ignore result {subImdb} {subTitle} not matching IMDB ID");
                    continue;
                }

                string subDate = dtOffset != null ? dtOffset?.ToString("g") : "";
                subInfo += String.Format("<br>{0} | {1} | {2}", subDate, subUploader, subFps);

                foreach (var fitem in files)
                {
                    string file = fitem.Name;
                    string fileExt = file.Split('.').LastOrDefault().ToLower();
                    if (fileExt != "srt" && fileExt != "sub") continue;

                    if (si.VideoType == VideoContentType.Episode)
                    {
                        Parser.EpisodeInfo epInfo = Parser.Episode.ParseTitle(file);
                        if (epInfo.EpisodeNumbers.Length > 0 && !epInfo.EpisodeNumbers.Contains(si.EpisodeNumber))
                        {
                            continue;
                        }
                    }

                    var item = new RemoteSubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = Download.GetId(subLink, file, si.LanguageInfo.TwoLetterISOLanguageName, ""),
                        ProviderName = Name,
                        Name = $"<a href='{subLink}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file}</a>",
                        Format = fileExt,
                        Author = subUploader,
                        Comment = subInfo,
                        //CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                        DownloadCount = int.Parse(subDownloads),
                        IsHashMatch = false,
#if EMBY
                        DateCreated = dtOffset,
                        IsForced = false,
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
