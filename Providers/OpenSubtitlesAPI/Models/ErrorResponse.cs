using System.Text.Json.Serialization;

namespace subbuzz.Providers.OpenSubtitlesAPI.Models
{
    public class ErrorResponse
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
