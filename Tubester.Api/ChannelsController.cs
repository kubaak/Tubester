using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Abstractions.Channels;
using Tubester.Application.Channels;
using Tubester.Domain;

namespace Tubester.Api;

/// <summary>Channels</summary>
[ApiController]
[Route("api/channels")]
[Tags("Channels")]
[Authorize]
public sealed class ChannelsController(
    IChannelSyncService channelSyncService)
    : ApiControllerBase
{
    /// <summary>
    /// Pulls channel details from YouTube and stores or updates the local channel snapshot.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("pull")]
    [ProducesResponseType(typeof(ChannelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelDto>> PullAsync(
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var channel = await channelSyncService.PullChannelAsync(
            userId,
            cancellationToken);

        var dto = ToDto(channel);

        return Ok(dto);
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
        if (result is null)
        {
            return Problem("User subscription is not active.", statusCode: StatusCodes.Status403Forbidden);
        }

        return Ok(result);
    }

    private ChannelDto ToDto(Channel channel)
    {
        return new ChannelDto(channel.ChannelId, channel.Name, channel.UploadsPlaylistId, channel.ETag);
    }
}