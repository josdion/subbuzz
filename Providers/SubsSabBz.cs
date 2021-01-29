using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System;
using MediaBrowser.Model.Globalization;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using SharpCompress.Readers;
using System.Linq;
using subbuzz.Helpers;

namespace subbuzz.Providers
{
    public class SubsSabBz : ISubtitleProvider, IHasOrder
    {
        private const string UrlSeparator = "*|*";

        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;

        public string Name => "subs.sab.bz";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };
        
        public int Order => 1;

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
                string[] ids = Utils.Base64UrlDecode(id).Split(new[] { UrlSeparator }, StringSplitOptions.None);
                string link = ids[0];
                string file = ids[1];
                string lang = ids[2];

                var opts = new HttpRequestOptions
                {
                    Url = link,
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:85.0) Gecko/20100101 Firefox/85.0",
                    Referer = "http://subs.sab.bz/",
                    TimeoutMs = 10000, //10 seconds timeout
                    EnableKeepAlive = false,
                };

                using (var response = await _httpClient.Get(opts).ConfigureAwait(false))
                {
                    var arcreader = ReaderFactory.Open(response);
                    while (arcreader.MoveToNextEntry())
                    {
                        if (file == arcreader.Entry.Key)
                        {
                            byte[] buf = new byte[arcreader.Entry.Size];
                            arcreader.OpenEntryStream().Read(buf, 0, buf.Length);

                            return new SubtitleResponse
                            {
                                Language = lang,
                                Format = arcreader.Entry.Key.Split('.').LastOrDefault().ToLower(),
                                IsForced = false,
                                Stream = new MemoryStream(buf)
                            };
                        }
                    }
                }
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
                    searchText = String.Format("{0} {1:D2} {2:D2}", ep.Series.OriginalTitle, request.ParentIndexNumber ?? 0, request.IndexNumber ?? 0);
                }
                else 
                { 
                    return res;  
                }

                var language = _localizationManager.FindLanguageInfo(request.Language.AsSpan());
                var lang = language.TwoLetterISOLanguageName.ToLower();

                _logger?.Info($"{Name} Request subtitle for '{searchText}', language={lang}, year={request.ProductionYear}");

                if (lang != "bg" && lang != "en")
                {
                    return res;
                }

                var opts = new HttpRequestOptions
                {
                    Url = "http://subs.sab.bz/index.php?",
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:85.0) Gecko/20100101 Firefox/85.0",
                    Referer = "http://subs.sab.bz/",
                    TimeoutMs = 10000, //10 seconds timeout
                    EnableKeepAlive = false,
                };

                var post_params = new Dictionary<string, string>
                {
                    { "act", "search"},
                    { "movie", searchText},
                    { "select-language", lang == "en" ? "1" : "2" },
                    { "upldr", "" },
                    { "yr", request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "" },
                    { "release", "" }
                };

                opts.SetPostData(post_params);

                using (var response = await _httpClient.Post(opts).ConfigureAwait(false))
                {
                    using (var reader = new StreamReader(response.Content, System.Text.Encoding.GetEncoding(1251)))
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
                            string subInfo = subNotes.Substring(subNotes.LastIndexOf("<b>Доп. инфо</b>")+17);

                            subInfo = Utils.TrimString(subInfo, "<br />");
                            subInfo = subInfo.Replace("<br /><br />", "<br />").Replace("<br /><br />", "<br />");

                            string subYear = linkNode.NextSibling.InnerText.Trim(new[] { ' ', '(', ')' });

                            string subDate = tdNodes[4].InnerText;
                            string subNumCd = tdNodes[6].InnerText;
                            string subFps = tdNodes[7].InnerText;
                            string subUploader = tdNodes[8].InnerText;
                            // subImdb =  tdNodes[9]
                            string subDownloads = tdNodes[10].InnerText;
                            string subRating = tdNodes[11].SelectSingleNode(".//img").Attributes["title"].Value;
                            subRating = subRating.Substring(subRating.LastIndexOf("Rating: ") + 8);

                            var files = await GetSubFileNames(subLink);
                            foreach (var file in files)
                            {
                                var item = new RemoteSubtitleInfo
                                {
                                    ThreeLetterISOLanguageName = language.ThreeLetterISOLanguageName,
                                    Id = Utils.Base64UrlEncode(subLink + UrlSeparator + file + UrlSeparator + language.TwoLetterISOLanguageName),
                                    ProviderName = $"[{Plugin.NAME}] <b>{Name}</b>",
                                    Name = file,
                                    Format = file.Split('.').LastOrDefault().ToUpper(),
                                    Author = subUploader,
                                    Comment = subInfo,
                                    //DateCreated = DateTimeOffset.Parse(subDate),
                                    CommunityRating = Convert.ToInt32(subRating),
                                    DownloadCount = Convert.ToInt32(subDownloads),
                                    IsHashMatch = false,
                                    IsForced = false,
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
                _logger.ErrorException(Name + ":Search:Exception:", e);
            }

            return res;
        }

        private async Task<IEnumerable<string>> GetSubFileNames(string link)
        {
            var res = new List<string>();

            var opts = new HttpRequestOptions
            {
                Url = link,
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:85.0) Gecko/20100101 Firefox/85.0",
                Referer = "http://subs.sab.bz/",
                TimeoutMs = 10000, //10 seconds timeout
                EnableKeepAlive = false,
            };

            try
            {
                using (var response = await _httpClient.Get(opts).ConfigureAwait(false))
                {
                    var arcreader = ReaderFactory.Open(response);
                    while (arcreader.MoveToNextEntry())
                    {
                        string fileExt = arcreader.Entry.Key.Split('.').LastOrDefault().ToLower();

                        if (!arcreader.Entry.IsDirectory && (fileExt == "srt" || fileExt == "sub"))
                        {
                            res.Add(arcreader.Entry.Key);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.ErrorException(Name + ":GetSubFileNames:Exception:", e);
            }

            return res;
        }


    }
}
