using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YouTubester.Application.Channels;

namespace YouTubester.Api;

/// <summary>Channels</summary>
[ApiController]
[Route("api/channels")]
[Tags("Channels")]
[Authorize]
public sealed class ChannelsController(
    IChannelSyncService channelSyncService)
    : ControllerBase
{
    /// <summary>
    /// Immediately synchronizes the current channel for the currently signed-in user.
    /// The current channel is resolved from the channel context (yt_channel_id claim).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("sync/current")]
    [ProducesResponseType(typeof(ChannelSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelSyncResult>> SyncCurrentChannelAsync(
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var result = await channelSyncService.SyncChannelAsync(userId, cancellationToken);
        return Ok(result);
    }
}