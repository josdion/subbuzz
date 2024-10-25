using System.Net;

namespace subbuzz.Providers.SubdlApi.Models
{
    public class HttpResponse
    {
        public HttpStatusCode Code { get; set; }

        public string Body { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public HttpResponse()
        {
        }

    }
}
