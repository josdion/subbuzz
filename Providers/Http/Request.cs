using subbuzz.Extensions;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.Http
{
    public class Request
    {
        public string Url { get; set; } = string.Empty;
        public string Referer { get; set; } = string.Empty;
        public RequestType Type { get; set; } = RequestType.GET;
        public Dictionary<string, string> PostParams { get; set; } = null;
        public Dictionary<string, string> Cookies { get; set; } = null;

        public enum RequestType
        {
            GET = 1,
            POST = 2,
        }

        public HttpMethod GetHttpMethod()
        {
            if (Type == RequestType.POST) return HttpMethod.Post;
            return HttpMethod.Get;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(Type == RequestType.POST ? "POST" : "GET");

            sb.Append(", Uri: ");
            sb.Append(Url);

            sb.Append(", Referer: ");
            sb.Append(Referer);

            sb.Append(", PostParams: ");
            sb.Append(PostParams == null ? "<null>" : $"{{ {string.Join(",", PostParams)} }}");

            sb.Append(", Cookies: ");
            sb.Append(Cookies == null ? "<null>" : $"{{ {string.Join(",", Cookies)} }}");

            return sb.ToString();
        }
    }

    public class RequestCached : Request
    {
        public string CacheKey { get; set; } = null;
        public string[] CacheRegion { get; set; }

        [JsonIgnore]
        public int CacheLifespan { get; set; }

        public override string ToString()
        {
            return base.ToString();
        }
    }

    public class RequestSub : RequestCached
    {
        public string File { get; set; } = string.Empty;
        public string Lang { get; set; } = string.Empty;
        public float? Fps { get; set; } = null;
        public float? FpsVideo { get; set; } = null;

        public string GetId()
        {
            return Utils.Base64UrlEncode<RequestSub>(this);
        }

        public static RequestSub FromId(string id)
        {
            if (id.IsNotNullOrWhiteSpace())
                return Utils.Base64UrlDecode<RequestSub>(id);

            return default;
        }

        public static float? FpsFromStr(string fps)
        {
            try
            {
                var f = float.Parse(fps, CultureInfo.InvariantCulture);
                return f < 1 ? null : f;
            }
            catch
            {
                return null;
            }
        }

        public override string ToString()
        {
            return base.ToString();
        }
    }

}
