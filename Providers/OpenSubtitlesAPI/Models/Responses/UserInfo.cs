using System.Text.Json.Serialization;

namespace subbuzz.Providers.OpenSubtitlesAPI.Models.Responses
{
    public class UserInfo
    {
        [JsonPropertyName("allowed_downloads")]
        public int AllowedDownloads { get; set; }

        [JsonPropertyName("level")]
        public string Level { get; set; }

        [JsonPropertyName("user_id")]
        public int UserID { get; set; }

        [JsonPropertyName("ext_installed")]
        public bool ExtInstalled { get; set; }

        [JsonPropertyName("vip")]
        public bool Vip { get; set; }

        [JsonPropertyName("downloads_count")]
        public int? DownloadCount { get; set; }

        [JsonPropertyName("remaining_downloads")]
        public int? RemainingDownloads { get; set; }
    }
}
