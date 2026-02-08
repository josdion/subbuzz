using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.SubSourceApi.Models.Responses
{
    public class SubtitleResult
    {
        [JsonPropertyName("subtitleId")]
        public int SubtitleId { get; set; }

        [JsonPropertyName("movieId")]
        public int MovieId { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("releaseInfo")]
        public List<string> ReleaseInfo { get; set; } = new List<string>();

        [JsonPropertyName("commentary")]
        public string Commentary { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public int Files { get; set; }

        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("hearingImpaired")]
        public bool HearingImpaired { get; set; }

        [JsonPropertyName("foreignParts")]
        public bool ForeignParts { get; set; }

        [JsonPropertyName("framerate")]
        public string Framerate { get; set; } = string.Empty;

        [JsonPropertyName("productionType")]
        public string ProductionType { get; set; } = string.Empty;

        [JsonPropertyName("releaseType")]
        public string ReleaseType { get; set; } = string.Empty;

        [JsonPropertyName("downloads")]
        public int Downloads { get; set; }

        [JsonPropertyName("comments")]
        public int Comments { get; set; }

        [JsonPropertyName("rating")]
        public Rating? Rating { get; set; }

        [JsonPropertyName("preview")]
        public string Preview { get; set; } = string.Empty;

        [JsonPropertyName("uploaderId")]
        public int UploaderId { get; set; }

        [JsonPropertyName("contributors")]
        public List<Contributor> Contributors { get; set; } = new List<Contributor>();

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = string.Empty;
    }
}
