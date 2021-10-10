using System.Text.Json.Serialization;

namespace subbuzz.Providers.OpenSubtitlesAPI.Models.Responses
{
    public class DownloadInfo
    {
        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("remaining")]
        public int Remaining { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
