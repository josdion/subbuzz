
namespace subbuzz.Providers.OpenSubtitlesAPI.Models
{
    public class LoginInfoInput
    {
        public string Username { get; set; } = null!;

        public string Password { get; set; } = null!;

        public string ApiKey { get; set; } = null!;
    }
}
