using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.OpenSubtitlesAPI.Models.Responses
{
    public class Attributes
    {
        [JsonPropertyName("language")]
        public string Language { get; set; }

        [JsonPropertyName("download_count")]
        public int DownloadCount { get; set; }

        [JsonPropertyName("hearing_impaired")]
        public bool? HearingImpaired { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; }

        [JsonPropertyName("fps")]
        public float? Fps { get; set; }

        [JsonPropertyName("ratings")]
        public float Ratings { get; set; }

        [JsonPropertyName("from_trusted")]
        public bool? FromTrusted { get; set; }

        [JsonPropertyName("foreign_parts_only")]
        public bool? ForeignPartsOnly { get; set; }

        [JsonPropertyName("upload_date")]
        public DateTime UploadDate { get; set; }

        [JsonPropertyName("release")]
        public string Release { get; set; }

        [JsonPropertyName("comments")]
        public string Comments { get; set; }

        [JsonPropertyName("uploader")]
        public Uploader Uploader { get; set; }

        [JsonPropertyName("feature_details")]
        public FeatureDetails FeatureDetails { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("files")]
        public IReadOnlyList<File> Files { get; set; } = Array.Empty<File>();

        [JsonPropertyName("moviehash_match")]
        public bool? MovieHashMatch { get; set; }

        [JsonPropertyName("ai_translated")]
        public bool? AiTranslated { get; set; }

        [JsonPropertyName("machine_translated")]
        public bool? MachineTranslated { get; set; }
    }
}
