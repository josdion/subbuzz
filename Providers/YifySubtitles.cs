﻿using System;
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
using subbuzz.Helpers;

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
    public class YifySubtitles : ISubtitleProvider, IHasOrder
    {
        private const string NAME = "YIFY Subtitles";
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
            var res = new List<RemoteSubtitleInfo>();

            try
            {
                if (!Plugin.Instance.Configuration.EnableYifySubtitles)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager);
                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                string url = "";

                if (!String.IsNullOrWhiteSpace(si.ImdbId))
                {
                    // search by IMDB Id
                    url = String.Format($"{ServerUrl}/movie-imdb/{si.ImdbId}");
                }
                else
                {
                    // TODO: url = $"{ServerUrl}/search?q={HttpUtility.UrlEncode(si.SearchText)};
                    _logger.LogInformation($"{NAME}: IMDB ID missing");
                    return res;
                }

                _logger.LogInformation($"{NAME}: GET {url}");

                using (var html = await downloader.GetStream(url, HttpReferer, null, cancellationToken))
                {
                    var subs = ParseHtml(html, si);
                    res.AddRange(subs);
                }

            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Search error: {e}");
            }

            return res;
        }

        protected IEnumerable<RemoteSubtitleInfo> ParseHtml(System.IO.Stream html, SearchInfo si)
        {
            var res = new List<RemoteSubtitleInfo>();

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

                var item = new RemoteSubtitleInfo
                {
                    ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                    Id = Download.GetId(subLink, "", si.LanguageInfo.TwoLetterISOLanguageName, si.VideoFps.ToString()),
                    ProviderName = Name,
                    Name = $"<a href='{ServerUrl}{subLinkPage}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{subTitle}</a>",
                    Format = "SRT",
                    Author = subUploader,
                    Comment = subInfo,
                    //DateCreated = DateTimeOffset.Parse(subDate),
                    CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                    //DownloadCount = int.Parse(subDownloads),
                    IsHashMatch = false,
#if EMBY
                    IsForced = false,
#endif
                };

                res.Add(item);
            }

            return res;
        }

    }
}
