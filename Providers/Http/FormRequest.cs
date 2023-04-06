using subbuzz.Extensions;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Web;

namespace subbuzz.Providers.Http
{
    public enum RequestType
    {
        GET = 1,
        POST = 2,
    }

    public class FormRequest
    {
        public string Url { get; set; } = string.Empty;
        public string Referer { get; set; } = string.Empty;
        public RequestType Type { get; set; } = RequestType.GET;
        public Dictionary<string, string> Params { get; set; } = null;

        public HttpMethod GetHttpMethod()
        {
            if (Type == RequestType.POST) return HttpMethod.Post;
            return HttpMethod.Get;
        }

        public string BuildUrl()
        {
            if (Type != RequestType.GET || Params.IsNullOrEmpty())
            {
                return Url;
            }

            var url = new StringBuilder(Url.TrimEnd('?')).Append('?');
            foreach (var op in Params.OrderBy(x => x.Key))
            {
                url.Append(HttpUtility.UrlEncode(op.Key)).Append('=').Append(HttpUtility.UrlEncode(op.Value)).Append('&');
            }

            url.Length -= 1; // Remove last &
            return url.ToString();
        }

        public HttpRequestMessage GetHttpRequestMessage(string redirectUri = null, HttpMethod redirectMethod = null)
        {
            HttpMethod method = redirectMethod ?? GetHttpMethod();
            var reqMsg = new HttpRequestMessage(method, redirectUri ?? BuildUrl());
            reqMsg.Headers.Host = reqMsg.RequestUri.Host;

            if (method == HttpMethod.Post && Params.IsNotNullOrEmpty())
                reqMsg.Content = new FormUrlEncodedContent(Params);

            if (Referer.IsNotNullOrWhiteSpace()) 
                reqMsg.Headers.Add("Referer", Referer);
            
            return reqMsg;
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
            sb.Append(Params == null ? "<null>" : $"{{ {string.Join(",", Params)} }}");

            return sb.ToString();
        }
    }

    public class RequestCached : FormRequest
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

        [JsonIgnore]
        public string FpsAsString 
        { 
            get
            {
                if (Fps == null) return string.Empty;
                return Fps?.ToString(CultureInfo.InvariantCulture);
            }
            set
            {
                Fps = FpsFromStr(value);
            }
        }

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

        public override string ToString()
        {
            return base.ToString();
        }

        private static float? FpsFromStr(string fps)
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
    }

}
