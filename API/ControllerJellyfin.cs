using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using subbuzz.Providers.OpenSubtitlesAPI;
using subbuzz.Providers.OpenSubtitlesAPI.Models;
using System;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.API
{
    [ApiController]
    [Produces(MediaTypeNames.Application.Json)]
    [Authorize(Policy = "DefaultAuthorization")]
    public class ControllerJellyfin : ControllerBase
    {
        [HttpPost("subbuzz/ValidateOpenSubtitlesLoginInfo")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> ValidateOpenSubtitlesLoginInfo([FromBody] LoginInfoInput body)
        {
            try
            {
                var response = await OpenSubtitles.LogInAsync(body.Username, body.Password, body.ApiKey, CancellationToken.None).ConfigureAwait(false);

                if (!response.Ok)
                {
                    var msg = $"{response.Code}{(response.Body.Length < 150 ? $" - {response.Body}" : string.Empty)}";

                    if (response.Body.Contains("message\":", StringComparison.Ordinal))
                    {
                        var err = JsonSerializer.Deserialize<ErrorResponse>(response.Body);
                        if (err != null)
                        {
                            msg = string.Equals(err.Message, "You cannot consume this service", StringComparison.Ordinal)
                                ? "Invalid API key provided" : err.Message;
                        }
                    }

                    return Unauthorized(new
                    {
                        Message = msg
                    });
                }

                return Ok(new
                {
                    Token = response.Data?.Token,
                    Downloads = response.Data?.User?.AllowedDownloads ?? 0
                });
            }
            catch
            {
                return Unauthorized(new
                {
                    Message = "Unable to verify OpenSubtitles.com account"
                });
            }
        }
    }
}
