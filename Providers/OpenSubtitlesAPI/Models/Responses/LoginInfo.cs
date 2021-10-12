using System;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.OpenSubtitlesAPI.Models.Responses
{
    public class LoginInfo
    {
        [JsonPropertyName("user")]
        public UserInfo User { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; }
    }
}
