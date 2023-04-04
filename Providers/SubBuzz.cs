using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Extensions;
using subbuzz.Helpers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

#if EMBY
using MediaBrowser.Model.Logging;
using ILogger = MediaBrowser.Model.Logging.ILogger;
using MediaBrowser.Common.Net;
using IHttpClient = MediaBrowser.Common.Net.IHttpClient;
#else
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILoggerFactory;
using System.Net.Http;
using IHttpClient = System.Net.Http.IHttpClientFactory;
#endif

namespace subbuzz.Providers
{
    public class SubBuzz : ISubtitleProvider, IHasOrder
    {
        public string Name => $"{Plugin.NAME}";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

        private readonly Logger _logger;
        private readonly Dictionary<string, ISubBuzzProvider> Providers;

        public SubBuzz(
            ILogger logger,
            IFileSystem fileSystem,
            ILocalizationManager localizationManager,
            ILibraryManager libraryManager,
            IHttpClient http
            )
        {
            _logger = new Logger(logger, typeof(SubBuzz).FullName);
            Plugin.Instance.InitCache();
            Providers = new Dictionary<string, ISubBuzzProvider>
            {
                { SubsSabBz.NAME,           new SubsSabBz(_logger.GetLogger<SubsSabBz>(), fileSystem, localizationManager, libraryManager) },
                { SubsUnacsNet.NAME,        new SubsUnacsNet(_logger.GetLogger<SubsUnacsNet>(), fileSystem, localizationManager, libraryManager) },
                { YavkaNet.NAME,            new YavkaNet(_logger.GetLogger<YavkaNet>(), fileSystem, localizationManager, libraryManager) },
                { OpenSubtitlesCom.NAME,    new OpenSubtitlesCom(_logger.GetLogger<OpenSubtitlesCom>(), fileSystem, localizationManager, libraryManager, http) },
                { PodnapisiNet.NAME,        new PodnapisiNet(_logger.GetLogger<PodnapisiNet>(), fileSystem, localizationManager, libraryManager) },
                { Subf2m.NAME,              new Subf2m(_logger.GetLogger<Subf2m>(), fileSystem, localizationManager, libraryManager) },
                { Subscene.NAME,            new Subscene(_logger.GetLogger<Subscene>(), fileSystem, localizationManager, libraryManager) },
                { YifySubtitles.NAME,       new YifySubtitles(_logger.GetLogger<YifySubtitles>(), fileSystem, localizationManager, libraryManager) },
                { Addic7ed.NAME,            new Addic7ed(_logger.GetLogger<Addic7ed>(), fileSystem, localizationManager, libraryManager) },
            };
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            foreach (var p in Providers)
            {
                if (id.StartsWith(p.Key))
                {
                    return await p.Value.GetSubtitles(id.Substring(p.Key.Length), cancellationToken);
                }
            }

            return new SubtitleResponse();
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var tasks = new Dictionary<string, Task<IEnumerable<RemoteSubtitleInfo>>>();

            _logger.LogInformation($"Start subtitle search for {request.Name}.");

            foreach (var p in Providers)
            {
                if (!p.Value.SupportedMediaTypes.Contains(request.ContentType)) continue;
                tasks.Add(p.Key, p.Value.Search(request, cancellationToken));
            }

            var res = new List<SubtitleInfo>();

            foreach (var task in tasks)
            {
#if JELLYFIN
                // Jellyfin search request times out after 30 seconds, so ignore searches not completed in time.
                var elapsedTime = watch.ElapsedMilliseconds;
                if (!task.Value.Wait((int)(elapsedTime >= 29000 ? 1 : 29000 - elapsedTime)))
                {
                    _logger.LogInformation($"The response from {task.Key} is ignored because it did not complete in time.");
                    continue;
                }
#endif

                List<SubtitleInfo> subs = (List<SubtitleInfo>)await task.Value;

                foreach (var s in subs)
                {
                    s.Id = task.Key + s.Id;
                    s.SubBuzzProviderName = task.Key;
                    s.ProviderName = Name;
#if NO_HTML
                    var parser = new AngleSharp.Html.Parser.HtmlParser();
                    var nodeList = parser.ParseFragment(s.Name, null);
                    s.Name = string.Concat(nodeList.Select(x => x.TextContent));
                    
                    if (s.IsForced ?? false) s.Name = "[Forced] " + s.Name;
                    if (s.Sdh ?? false) s.Name = "[SDH] " + s.Name;

                    var regex = new System.Text.RegularExpressions.Regex(@"<br.*?>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    s.Comment = regex.Replace(s.Comment, " &#9734; ");

                    nodeList = parser.ParseFragment(s.Comment, null);
                    s.Comment = $"[{task.Key}] " + string.Concat(nodeList.Select(x => x.TextContent));
#else
                    s.Comment = $"<b>[{task.Key}]</b> " + s.Comment;
                    if (s.Sdh ?? false) 
                        s.Name =
                            "<div class=\"inline-flex align-items-center justify-content-center mediaInfoItem\">" +
                            "<i class=\"md-icon button-icon button-icon-left secondaryText\" style=\"font-size:1.4em;\">hearing_disabled</i>" +
                            s.Name +
                            "</div>";
#endif
                }

                Utils.MergeSubtitleInfo(res, subs);
            }

            if (request.IsPerfectMatch)
            {
                res.RemoveAll(i => (i.IsHashMatch ?? false) == false);
            }

#if EMBY
            if (request.IsForced ?? false)
            {
                res.RemoveAll(i => (i.IsForced ?? false) == false);
            }
#endif

            res.Sort((x, y) => y.Score.CompareTo(x.Score));

            watch.Stop();
            _logger.LogInformation($"Search duration: {watch.ElapsedMilliseconds/1000.0} sec. Subtitles found: {res.Count}");

            return res;
        }
    }
}
