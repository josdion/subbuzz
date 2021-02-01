using HtmlAgilityPack;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Web;

#if EMBY
using subbuzz.Logging;
using ILogger = MediaBrowser.Model.Logging.ILogger;
#else
using System.Net.Http;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger<subbuzz.Providers.SubsSabBz>;
#endif

namespace subbuzz.Providers
{
    public class SubsSabBz : ISubtitleProvider, IHasOrder
    {
        private const string HttpReferer = "http://subs.sab.bz/index.php?";
        private readonly List<string> Languages = new List<string> { "bg", "en" };

        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;

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

        public string Name => $"[{Plugin.NAME}] <b>subs.sab.bz</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

        public SubsSabBz(ILogger logger, IFileSystem fileSystem, IHttpClient httpClient,
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
                return await Download.GetArchiveSubFile(_httpClient, id, HttpReferer, Encoding.GetEncoding(1251)).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"GetSubtitles error: {e}");
            }

            return new SubtitleResponse();
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            var res = new List<RemoteSubtitleInfo>();

            try
            {
                SearchInfo si = SearchInfo.GetSearchInfo(
                    request, 
                    _localizationManager, 
                    _libraryManager, 
                    "{0} {1:D2}x{2:D2}",
                    InconsistentTvs,
                    InconsistentMovies);

                _logger.LogInformation($"Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                if (!Languages.Contains(si.Lang) || String.IsNullOrEmpty(si.SearchText))
                {
                    return res;
                }

                var opts = new HttpRequestOptions
                {
                    Url = "http://subs.sab.bz/index.php?",
                    UserAgent = Download.UserAgent,
                    Referer = HttpReferer,
                    EnableKeepAlive = false,
                    CancellationToken = cancellationToken,
#if EMBY
                    TimeoutMs = 10000, //10 seconds timeout
#endif
                };

                var post_params = new Dictionary<string, string>
                {
                    { "act", "search"},
                    { "movie", si.SearchText },
                    { "select-language", si.Lang == "en" ? "1" : "2" },
                    { "upldr", "" },
                    { "yr", request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "" },
                    { "release", "" }
                };

#if EMBY
                post_params["movie"] = HttpUtility.UrlEncode(si.SearchText);
                opts.SetPostData(post_params);
#else
                ByteArrayContent formUrlEncodedContent = new FormUrlEncodedContent(post_params);
                opts.RequestContent = await formUrlEncodedContent.ReadAsStringAsync();
                opts.RequestContentType = "application/x-www-form-urlencoded";
#endif

                using (var response = await _httpClient.Post(opts).ConfigureAwait(false))
                {
                    using (var reader = new StreamReader(response.Content, Encoding.GetEncoding(1251)))
                    {
                        var html = await reader.ReadToEndAsync().ConfigureAwait(false);

                        var htmlDoc = new HtmlDocument();
                        htmlDoc.LoadHtml(html);

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
                            // subImdb =  tdNodes[9]
                            string subDownloads = tdNodes[10].InnerText;

                            string subRating = "0";
                            var rtImgNode = tdNodes[11].SelectSingleNode(".//img");
                            if (rtImgNode != null)
                            {
                                subRating = rtImgNode.Attributes["title"].Value;
                                subRating = subRating.Replace("Rating: ", "").Trim();
                            }

                            var files = await Download.GetArchiveSubFileNames(_httpClient, subLink, HttpReferer).ConfigureAwait(false);
                            foreach (var file in files)
                            {
                                string fileExt = file.Split('.').LastOrDefault().ToLower();
                                if (fileExt != "srt" && fileExt != "sub") continue;

                                var item = new RemoteSubtitleInfo
                                {
                                    ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                                    Id = Download.GetId(subLink, file, si.LanguageInfo.TwoLetterISOLanguageName, subFps),
                                    ProviderName = Name,
                                    Name = file,
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
            catch (Exception e)
            {
                _logger.LogError(e, $"Search error: {e}");
            }

            return res;
        }

    }
}
