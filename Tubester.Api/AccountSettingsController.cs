using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Abstractions.Account;
using Tubester.Application.Account;

namespace Tubester.Api;

/// <summary>Account Settings</summary>
[ApiController]
[Route("api/settings/account")]
[Tags("AccountSettings")]
[Authorize]
public sealed class AccountSettingsController(
    IAccountSettingsService accountSettingsService,
    ICurrentUserContext currentUserContext)
    : ApiControllerBase
{
    /// <summary>
    /// Returns account settings for the current authenticated user.
    /// If settings do not exist yet, they are created with defaults.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AccountSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccountSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var userId = currentUserContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var settings = await accountSettingsService.GetOrCreateAsync(userId, cancellationToken);
        return Ok(settings);
    }

    /// <summary>
    /// Updates the account settings for the current authenticated user.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(AccountSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccountSettingsDto>> UpdateSettings(
        [FromBody] UpdateAccountSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var settings = await accountSettingsService.UpdateAsync(userId, request, cancellationToken);
        return Ok(settings);
    }
}
