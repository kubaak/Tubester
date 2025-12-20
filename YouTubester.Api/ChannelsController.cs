using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YouTubester.Abstractions.Channels;
using YouTubester.Application;
using YouTubester.Application.Channels;
using YouTubester.Domain;

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
    /// Pulls channel metadata from YouTube (by channel id)
    /// </summary>
    /// <param name="channelId">Channel id</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The persisted <see cref="Channel"/>.</returns>
    [HttpPost("pull/{channelId}")]
    [ProducesResponseType(typeof(Channel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Channel>> PullAsync([FromRoute] string channelId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return ValidationProblem("'channelId' is required.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var channel = await channelSyncService.PullChannelAsync(userId, channelId, cancellationToken);
        return Ok(channel);
    }

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