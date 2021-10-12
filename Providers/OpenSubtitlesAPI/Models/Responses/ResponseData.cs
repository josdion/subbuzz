using System.Text.Json.Serialization;

namespace subbuzz.Providers.OpenSubtitlesAPI.Models.Responses
{
    public class ResponseData
    {
        [JsonPropertyName("attributes")]
        public Attributes Attributes { get; set; }
    }
}
