using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tubester.Api;

/// <summary>
/// Authentication Controller
/// </summary>
[ApiController]
[Route("api/auth")]
[Tags("Authentication")]
[Authorize]
public sealed class AuthController : ControllerBase
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="returnUrl"></param>
    /// <returns></returns>
    [HttpGet("login/google")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult LoginWithGoogle([FromQuery] string? returnUrl = "/")
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
        {
            returnUrl = "/";
        }

        var authenticationProperties = new AuthenticationProperties { RedirectUri = returnUrl };

        return Challenge(authenticationProperties, "GoogleRead");
    }

    /// <summary>
    /// Starts the explicit Google write-consent flow.
    /// </summary>
    /// <param name="returnUrl">Local URL to redirect to after consent is granted.</param>
    /// <returns></returns>
    [HttpGet("login/google/write")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult StartWriteConsent([FromQuery] string? returnUrl = "/")
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
        {
            returnUrl = "/";
        }

        var authenticationProperties = new AuthenticationProperties { RedirectUri = returnUrl };

        return Challenge(authenticationProperties, "GoogleWrite");
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    [HttpGet("me")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AuthMeResponse), StatusCodes.Status200OK)]
    public ActionResult<AuthMeResponse> Me()
    {
        var name = User.Identity?.Name;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        var subject = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var googlePicture = User.FindFirst("picture")?.Value;

        var channelId = User.FindFirst("yt_channel_id")?.Value;
        var channelTitle = User.FindFirst("yt_channel_title")?.Value;
        var channelPicture = User.FindFirst("yt_channel_picture")?.Value;

        var picture = string.IsNullOrWhiteSpace(channelPicture)
            ? googlePicture
            : channelPicture;

        var hasWriteAccess = User.HasClaim("yt_write_granted", "true");

        return Ok(new
        {
            name,
            email,
            sub = subject,
            channelId,
            channelTitle,
            picture,
            hasWriteAccess
        });
    }

    /// <summary>
    /// 
    /// </summary>
    public sealed class AuthMeResponse
    {
        /// <summary>
        /// 
        /// </summary>
        public string? Name { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public string? Email { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public string? Sub { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public string? ChannelId { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public string? ChannelTitle { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public string? Picture { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public bool HasWriteAccess { get; init; }
    }
}