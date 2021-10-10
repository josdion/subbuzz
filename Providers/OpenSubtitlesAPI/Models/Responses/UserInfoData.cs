using System.Text.Json.Serialization;

namespace subbuzz.Providers.OpenSubtitlesAPI.Models.Responses
{
    public class UserInfoData
    {
        [JsonPropertyName("data")]
        public UserInfo? Data { get; set; }
    }
}
