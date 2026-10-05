using subbuzz.Extensions;
using subbuzz.Providers.Http;
using subbuzz.Providers.TitloviAPI.Models;
using subbuzz.Providers.TitloviAPI.Models.Responses;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace subbuzz.Providers.TitloviAPI
{
    public static class Titlovi
    {
        public const string ServerUrl = "https://titlovi.com";
        private const string BaseApiUrl = "https://kodi.titlovi.com/api/subtitles";

        public const int TypeMovie = 1;
        public const int TypeEpisode = 2;

        // Language names used by titlovi.com, keyed by the two letter ISO language code.
        // Serbian subtitles are published in both Latin (Srpski) and Cyrillic (Cirilica) script.
        private static readonly Dictionary<string, string[]> Languages = new Dictionary<string, string[]>
        {
            { "bs", new[] { "Bosanski" } },
            { "en", new[] { "English" } },
            { "hr", new[] { "Hrvatski" } },
            { "mk", new[] { "Makedonski" } },
            { "sl", new[] { "Slovenski" } },
            { "sr", new[] { "Srpski", "Cirilica" } },
        };

        private static readonly Regex JsonDateRegex = new Regex(@"\\?/Date\((?<ms>-?\d+)(?:[+-]\d{4})?\)\\?/", RegexOptions.Compiled);
        private static readonly Regex SlugRegex = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled);

        public static RequestHelper RequestHelperInstance { get; set; }

        public static bool IsLanguageSupported(string lang)
        {
            return lang.IsNotNullOrWhiteSpace() && Languages.ContainsKey(lang.Trim().ToLowerInvariant());
        }

        // Returns the language names expected by the search API, e.g. "Srpski|Cirilica" for "sr".
        public static string GetSearchLanguages(string lang)
        {
            if (lang.IsNullOrWhiteSpace() || !Languages.TryGetValue(lang.Trim().ToLowerInvariant(), out var names))
            {
                return string.Empty;
            }

            return string.Join("|", names);
        }

        // Converts a language name returned by the API to the two letter ISO code, or null if unknown.
        public static string GetLanguageCode(string titloviLang)
        {
            if (titloviLang.IsNullOrWhiteSpace())
            {
                return null;
            }

            foreach (var lang in Languages)
            {
                if (lang.Value.Any(name => name.EqualsIgnoreCase(titloviLang.Trim())))
                {
                    return lang.Key;
                }
            }

            return null;
        }

        public static async Task<ApiResponse<LoginInfo>> LogInAsync(string username, string password, CancellationToken cancellationToken)
        {
            var options = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password),
                new KeyValuePair<string, string>("json", "true"),
            };

            // The API expects the credentials as query parameters of a POST request without a body.
            var url = BuildQueryString("/gettoken", options);
            var response = await SendRequestAsync(url, HttpMethod.Post, 1, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<LoginInfo>(response, "login");
        }

        public static async Task<ApiResponse<SearchResponse>> SearchAsync(
            IEnumerable<KeyValuePair<string, string>> options, string token, int userId, CancellationToken cancellationToken)
        {
            var opts = new List<KeyValuePair<string, string>>(options)
            {
                new KeyValuePair<string, string>("token", token),
                new KeyValuePair<string, string>("userid", userId.ToString(CultureInfo.InvariantCulture)),
            };

            var url = BuildQueryString("/search", opts);
            var response = await SendRequestAsync(url, HttpMethod.Get, 1, cancellationToken).ConfigureAwait(false);

            // the token is left out of the context, so it never ends up in the logs
            return new ApiResponse<SearchResponse>(response, $"url: {BuildQueryString("/search", options)}");
        }

        public static List<KeyValuePair<string, string>> BuildSearchOptions(
            string query, string lang, bool isEpisode, int? season, int? episode, string imdbId, int page)
        {
            var options = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("query", query),
                new KeyValuePair<string, string>("lang", GetSearchLanguages(lang)),
                new KeyValuePair<string, string>("type", (isEpisode ? TypeEpisode : TypeMovie).ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("json", "true"),
            };

            if (isEpisode)
            {
                if (season != null)
                {
                    options.Add(new KeyValuePair<string, string>("season", season.Value.ToString(CultureInfo.InvariantCulture)));
                }

                if (episode != null)
                {
                    options.Add(new KeyValuePair<string, string>("episode", episode.Value.ToString(CultureInfo.InvariantCulture)));

                    // episode 0 stands for season packs, which may contain the requested episode
                    if (episode.Value > 0)
                    {
                        options.Add(new KeyValuePair<string, string>("episode", "0"));
                    }
                }
            }

            if (imdbId.IsNotNullOrWhiteSpace())
            {
                options.Add(new KeyValuePair<string, string>("imdbId", imdbId));
            }

            if (page > 1)
            {
                options.Add(new KeyValuePair<string, string>("pg", page.ToString(CultureInfo.InvariantCulture)));
            }

            return options;
        }

        public static string BuildQueryString(string path, IEnumerable<KeyValuePair<string, string>> param)
        {
            var sorted = param
                .Where(x => x.Value != null)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ThenBy(x => x.Value, StringComparer.Ordinal)
                .ToList();

            if (sorted.Count == 0)
            {
                return path;
            }

            var url = new StringBuilder(path);
            url.Append('?');
            foreach (var op in sorted)
            {
                url.Append(HttpUtility.UrlEncode(op.Key))
                    .Append('=')
                    .Append(HttpUtility.UrlEncode(op.Value))
                    .Append('&');
            }

            url.Length -= 1; // Remove last &
            return url.ToString();
        }

        public static string GetDownloadUrl(int mediaId, int type)
        {
            return $"{ServerUrl}/download/?type={type.ToString(CultureInfo.InvariantCulture)}&mediaid={mediaId.ToString(CultureInfo.InvariantCulture)}";
        }

        // The site accepts any slug in front of the subtitle id, the one built here just keeps the link readable.
        public static string GetPageUrl(string title, int mediaId)
        {
            string slug = (title ?? string.Empty).Replace('đ', 'd').Replace('Đ', 'D').RemoveAccent().ToLowerInvariant();
            slug = SlugRegex.Replace(slug, "-").Trim('-');
            if (slug.IsNullOrWhiteSpace())
            {
                slug = "titl";
            }

            return $"{ServerUrl}/titlovi/{slug}-{mediaId.ToString(CultureInfo.InvariantCulture)}/";
        }

        // Dates come as the local server time without an offset, so they are kept as unspecified kind.
        public static DateTime? ParseDate(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            var jsonDate = JsonDateRegex.Match(value);
            if (jsonDate.Success && long.TryParse(jsonDate.Groups["ms"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime dt))
            {
                return dt;
            }

            return null;
        }

        // A token is treated as expired a day in advance, as the expiration is in the time zone of titlovi.com.
        public static bool IsTokenExpired(string expiration, DateTime? now = null)
        {
            DateTime? dt = ParseDate(expiration);
            if (dt == null)
            {
                return true;
            }

            return (now ?? DateTime.Now) >= dt.Value.AddDays(-1);
        }

        public static string GetLoginErrorMessage<T>(ApiResponse<T> response)
        {
            if (response.Code == HttpStatusCode.Unauthorized)
            {
                return "Invalid username or password, or API access is not enabled for this titlovi.com account";
            }

            if (response.Ok)
            {
                return "titlovi.com did not return a token";
            }

            string body = response.Body ?? string.Empty;
            return body.Length > 0 && body.Length < 150 ? $"{response.Code} - {body}" : response.Code.ToString();
        }

        private static async Task<HttpResponse> SendRequestAsync(string endpoint, HttpMethod method, int attempt, CancellationToken cancellationToken)
        {
            var (body, _, httpStatusCode) = await RequestHelperInstance.SendRequestAsync(
                BaseApiUrl + endpoint, method, null, null, cancellationToken).ConfigureAwait(false);

            bool retriable = httpStatusCode == (HttpStatusCode)429 /*HttpStatusCode.TooManyRequests*/
                || httpStatusCode == HttpStatusCode.BadGateway
                || httpStatusCode == HttpStatusCode.ServiceUnavailable;

            if (retriable && attempt <= 3)
            {
                await Task.Delay(attempt * 1000, cancellationToken).ConfigureAwait(false);

                return await SendRequestAsync(endpoint, method, attempt + 1, cancellationToken).ConfigureAwait(false);
            }

            return new HttpResponse
            {
                Body = body,
                Code = httpStatusCode,
            };
        }
    }
}
