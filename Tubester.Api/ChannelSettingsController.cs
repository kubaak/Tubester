using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Abstractions.Channels;
using Tubester.Application.Channels;

namespace Tubester.Api;

/// <summary>Channel Settings</summary>
[ApiController]
[Route("api/channel-settings")]
[Tags("Channel Settings")]
[Authorize]
public sealed class ChannelSettingsController(
    IChannelSettingsService channelSettingsService,
    ICurrentChannelContext currentChannelContext)
    : ControllerBase
{
    /// <summary>
    /// Returns the comment assistant settings for the specified channel.
    /// If settings do not exist yet, they are created with defaults.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ChannelSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ChannelSettingsDto>> GetSettings(
        CancellationToken cancellationToken)
    {
        var channelId = currentChannelContext.ChannelId;
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return Unauthorized();
        }

        var settings = await channelSettingsService.GetOrCreateAsync(channelId, cancellationToken);
        return Ok(settings);
    }

    /// <summary>
    /// Fully updates the comment assistant settings for the specified channel.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(ChannelSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ChannelSettingsDto>> UpdateSettings(
        [FromBody] UpdateChannelSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var channelId = currentChannelContext.ChannelId;
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return Unauthorized();
        }

        var settings = await channelSettingsService.UpdateAsync(channelId, request, cancellationToken);
        return Ok(settings);
    }
}