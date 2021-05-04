using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Helpers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

#if EMBY
using subbuzz.Logging;
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
    public class SubBuzz : ISubtitleProvider, IHasOrder
    {
        public string Name => $"{Plugin.NAME}";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

        private Dictionary<string, ISubBuzzProvider> Providers;

        public SubBuzz(
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
            Providers = new Dictionary<string, ISubBuzzProvider>
            {
                { SubsSabBz.NAME,      new SubsSabBz(logger, fileSystem, localizationManager, libraryManager, http) },
                { SubsUnacsNet.NAME,   new SubsUnacsNet(logger, fileSystem, localizationManager, libraryManager, http) },
                { YavkaNet.NAME,       new YavkaNet(logger, fileSystem, localizationManager, libraryManager, http) },
                { YifySubtitles.NAME,  new YifySubtitles(logger, fileSystem, localizationManager, libraryManager, http) },
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
            var tasks = new Dictionary<string, Task<IEnumerable<RemoteSubtitleInfo>>>();

            foreach (var p in Providers)
            {
                if (!p.Value.SupportedMediaTypes.Contains(request.ContentType)) continue;
                tasks.Add(p.Key, p.Value.Search(request, cancellationToken));
            }

            var res = new List<SubtitleInfo>();

            foreach (var task in tasks)
            {
                List<SubtitleInfo> subs = (List<SubtitleInfo>)await task.Value;

                foreach (var s in subs)
                {
                    s.Id = task.Key + s.Id;
                    s.SubBuzzProviderName = task.Key;
                    s.ProviderName = Name;
                    s.Comment = $"<b>[{task.Key}]</b> " + s.Comment;
                }

                Utils.MergeSubtitleInfo(res, subs);
            }

            res.Sort((x, y) => y.Score.CompareTo(x.Score));
            return res;
        }
    }
}
