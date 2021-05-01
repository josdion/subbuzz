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
using System.Text;

#if EMBY
using subbuzz.Logging;
using ILogger = MediaBrowser.Model.Logging.ILogger;
#else
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger<subbuzz.Providers.SubsSabBz>;
#endif

#if JELLYFIN_10_7
using System.Net.Http;
#endif

namespace subbuzz.Providers
{
    public class SubsSabBz : ISubtitleProvider, IHasOrder
    {
        private const string NAME = "subs.sab.bz";
        private const string ServerUrl = "http://subs.sab.bz";
        private const string HttpReferer = "http://subs.sab.bz/index.php?";
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
            { "Marvel's Jessica Jones", "Jessica Jones" },
            { "DC's Legends of Tomorrow", "Legends of Tomorrow" },
            { "Doctor Who (2005)", "Doctor Who" },
            { "Star Trek: Deep Space Nine", "Star Trek DS9" },
            { "Star Trek: The Next Generation", "Star Trek TNG" },
        };

        private static Dictionary<string, string> InconsistentMovies = new Dictionary<string, string>
        {
            { "Back to the Future Part", "Back to the Future" },
        };

        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

        public SubsSabBz(
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
                if (!Plugin.Instance.Configuration.EnableSubssabbz)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(
                    request,
                    _localizationManager,
                    _libraryManager,
                    "{0} {1:D2}x{2:D2}",
                    "{0} Season {1}",
                    InconsistentTvs,
                    InconsistentMovies);

                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}, IMDB={si.ImdbId}");

                if (!Languages.Contains(si.Lang))
                {
                    return res;
                }

                // search by IMDB Id

                if (!String.IsNullOrWhiteSpace(si.ImdbId))
                {
                    string urlImdb = String.Format(
                        "{0}/index.php?act=search&movie={1}&select-language={2}&upldr=&yr=&&release=&imdb={3}&sort=dd&",
                        ServerUrl,
                        request.ContentType == VideoContentType.Episode ?
                            String.Format("{0:D2}x{1:D2}", request.ParentIndexNumber ?? 0, request.IndexNumber ?? 0) : "",
                        si.Lang == "en" ? "1" : "2",
                        si.ImdbId
                        );

                    using (var html = await downloader.GetStream(urlImdb, HttpReferer, null, cancellationToken))
                    {
                        var subs = await ParseHtml(html, si, cancellationToken);
                        res.AddRange(subs);
                    }

                    if (request.ContentType == VideoContentType.Episode && 
                        !String.IsNullOrWhiteSpace(si.ImdbIdEpisode) &&
                        si.ImdbId != si.ImdbIdEpisode)
                    {
                        string urlImdbEp = String.Format(
                            "{0}/index.php?act=search&movie=&select-language={1}&upldr=&yr=&&release=&imdb={2}&sort=dd&",
                            ServerUrl,
                            si.Lang == "en" ? "1" : "2",
                            si.ImdbIdEpisode
                            );

                        using (var html = await downloader.GetStream(urlImdbEp, HttpReferer, null, cancellationToken))
                        {
                            var subs = await ParseHtml(html, si, cancellationToken);
                            res.AddRange(subs);
                        }
                    }
                }

                if (String.IsNullOrEmpty(si.SearchText))
                {
                    return res;
                }

                // search by movies/series title

                var post_params = new Dictionary<string, string>
                {
                    { "act", "search"},
                    { "movie", si.SearchText },
                    { "select-language", si.Lang == "en" ? "1" : "2" },
                    { "upldr", "" },
                    { "yr", request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "" },
                    { "release", "" }
                };

                using (var html = await downloader.GetStream($"{ServerUrl}/index.php?", HttpReferer, post_params, cancellationToken))
                {
                    List<RemoteSubtitleInfo> subs = await ParseHtml(html, si, cancellationToken);
                    Utils.MergeSubtitleInfo(res, subs);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Search error: {e}");
            }

            return res;
        }

        protected async Task<List<RemoteSubtitleInfo>> ParseHtml(System.IO.Stream html, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<RemoteSubtitleInfo>();

            var htmlDoc = new HtmlDocument();
            htmlDoc.Load(html, Encoding.GetEncoding(1251), true);

            var trNodes = htmlDoc.DocumentNode.SelectNodes("//tr[@class='subs-row']");
            if (trNodes == null) return res;

            for (int i = 0; i < trNodes.Count; i++)
            {
                var tdNodes = trNodes[i].SelectNodes("td");
                if (tdNodes == null || tdNodes.Count < 12) continue;

                HtmlNode linkNode = tdNodes[3].SelectSingleNode("a[@href]");
                if (linkNode == null) continue;

                string subLink = linkNode.Attributes["href"].Value;
                var regexLink = new Regex(@"(?<g1>.*index.php\?)(?<g2>s=.*&amp;)?(?<g3>.*)");
                subLink = regexLink.Replace(subLink, "${g1}${g3}");

                string subTitle = linkNode.InnerText;

                string subNotes = linkNode.Attributes["onmouseover"].DeEntitizeValue;
                var regex = new Regex(@"ddrivetip\(\'<div.*/></div>(.*)\',\'#[0-9]+\'\)");
                subNotes = regex.Replace(subNotes, "$1");
                string subInfo = subNotes.Substring(subNotes.LastIndexOf("<b>Доп. инфо</b>") + 17);

                subInfo = Utils.TrimString(subInfo, "<br />");
                subInfo = subInfo.Replace("<br /><br />", "<br />").Replace("<br /><br />", "<br />");

                string subYear = linkNode.NextSibling.InnerText.Trim(new[] { ' ', '(', ')' });

                string subDate = tdNodes[4].InnerText;
                string subNumCd = tdNodes[6].InnerText;
                string subFps = tdNodes[7].InnerText;
                string subUploader = tdNodes[8].InnerText;

                int imdbId = 0;
                string subImdb = "";
                var linkImdb = tdNodes[9].SelectSingleNode("a[@href]");
                if (linkImdb != null)
                {
                    var reg = new Regex(@"imdb.com/title/(tt(\d+))/?$");
                    var match = reg.Match(linkImdb.Attributes["href"].Value);
                    if (match != null && match.Groups.Count > 2)
                    {
                        subImdb = match.Groups[1].Value;
                        imdbId = int.Parse(match.Groups[2].ToString());
                    }

                    if (!si.CheckImdbId(imdbId))
                    {
                        _logger.LogInformation($"{NAME}: Ignore result {subImdb} {subTitle} not matching IMDB ID");
                        continue;
                    }
                }

                string subDownloads = tdNodes[10].InnerText;

                string subRating = "0";
                var rtImgNode = tdNodes[11].SelectSingleNode(".//img");
                if (rtImgNode != null)
                {
                    subRating = rtImgNode.Attributes["title"].Value;
                    subRating = subRating.Replace("Rating: ", "").Trim();
                }

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
                        Format = file.Split('.').LastOrDefault().ToUpper(),
                        Author = subUploader,
                        Comment = subInfo,
                        //DateCreated = DateTimeOffset.Parse(subDate),
                        CommunityRating = Convert.ToInt32(subRating),
                        DownloadCount = Convert.ToInt32(subDownloads),
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
