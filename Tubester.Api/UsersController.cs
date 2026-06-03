using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Abstractions.Users;

namespace Tubester.Api;

/// <summary>
/// Controller for user account operations.
/// </summary>
[Route("api/users")]
[Tags("Users")]
[Authorize]
public sealed class UsersController(IUserDataDeletionService userDataDeletionService) : ApiControllerBase
{
    /// <summary>
    /// Deletes all user-owned data and all data obtained from or derived from Google/YouTube APIs.
    /// The authenticated user's account is marked as deleted, preserving only the minimal technical identity row.
    /// After deletion, auth cookies are cleared and the user must log in again to use the app.
    /// </summary>
    /// <returns>204 No Content on success.</returns>
    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteCurrentUser(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        await userDataDeletionService.DeleteUserDataAsync(userId, cancellationToken);

        // Sign out and clear auth cookies after successful deletion
        await SignOutAndDeleteAuthCookiesAsync();

        return NoContent();
    }

    private async Task SignOutAndDeleteAuthCookiesAsync()
    {
        await HttpContext.SignOutAsync();

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
}