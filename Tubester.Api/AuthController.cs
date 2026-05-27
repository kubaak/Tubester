using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tubester.Api;

/// <summary>
/// Authentication Controller
/// </summary>
[Route("api/auth")]
[Tags("Authentication")]
[Authorize]
public sealed class AuthController(IConfiguration configuration) : ApiControllerBase
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
    /// Logs out the user and redirects to a specified return URL.
    /// </summary>
    /// <param name="returnUrl">Optional local URL to redirect to after logout.
    /// If the return URL is not local, it defaults to "/"</param>
    /// <returns>An IActionResult that redirects to the provided return URL or the default URL.</returns>
    [HttpGet("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> LogoutRedirect([FromQuery] string? returnUrl)
    {
        await SignOutAndDeleteAuthCookiesAsync();

        var safeReturnUrl = Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/";

        return Redirect($"/login?returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
    }

    private async Task SignOutAndDeleteAuthCookiesAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        foreach (var cookie in Request.Cookies.Keys)
        {
            if (cookie.StartsWith(".AspNetCore.", StringComparison.OrdinalIgnoreCase))
            {
                Response.Cookies.Delete(cookie, new CookieOptions
                {
                    Path = "/",
                    Secure = true,
                    SameSite = SameSiteMode.None
                });
            }
        }
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

        var isAdmin = !string.IsNullOrWhiteSpace(email) && configuration["AdminEmails:0"]?.Contains(email) == true;

        return Ok(new AuthMeResponse
        {
            Name = name,
            Email = email,
            Sub = subject,
            ChannelId = channelId,
            ChannelTitle = channelTitle,
            Picture = picture,
            HasWriteAccess = hasWriteAccess,
            IsAdmin = isAdmin
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
        /// <summary>
        /// 
        /// </summary>
        public bool IsAdmin { get; init; }
    }
}