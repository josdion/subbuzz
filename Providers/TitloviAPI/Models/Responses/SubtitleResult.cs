using System.Text.Json.Serialization;

namespace subbuzz.Providers.TitloviAPI.Models.Responses
{
    public class SubtitleResult
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("Title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("Year")]
        public int? Year { get; set; }

        // 1 - movie, 2 - TV series
        [JsonPropertyName("Type")]
        public int Type { get; set; }

        // Direct download link of the subtitle archive
        [JsonPropertyName("Link")]
        public string Link { get; set; } = string.Empty;

        // Season and episode are 0 for season packs and not set for movies
        [JsonPropertyName("Season")]
        public int? Season { get; set; }

        [JsonPropertyName("Episode")]
        public int? Episode { get; set; }

        [JsonPropertyName("Special")]
        public int? Special { get; set; }

        // Language name as used by titlovi.com, e.g. Hrvatski, Srpski, Cirilica
        [JsonPropertyName("Lang")]
        public string Lang { get; set; } = string.Empty;

        [JsonPropertyName("Date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("DownloadCount")]
        public int? DownloadCount { get; set; }

        [JsonPropertyName("Rating")]
        public float? Rating { get; set; }

        [JsonPropertyName("Release")]
        public string Release { get; set; } = string.Empty;
    }
}
