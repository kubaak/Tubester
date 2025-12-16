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
    IChannelSyncService channelSyncService,
    ICommentScanService commentScanService,
    IChannelRepository channelRepository)
    : ControllerBase
{
    /// <summary>
    /// Returns all channels owned by the current user in this application.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserChannelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<UserChannelDto>>> GetUserChannelsAsync(
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var channels = await channelRepository.GetChannelsForUserAsync(userId, cancellationToken);
        var picture = User.FindFirst("picture")?.Value;

        var userChannels = new List<UserChannelDto>(channels.Count);
        foreach (var channel in channels)
        {
            var userChannel = new UserChannelDto(
                channel.ChannelId,
                channel.Name,
                picture);
            userChannels.Add(userChannel);
        }

        return Ok(userChannels);
    }

    /// <summary>
    /// Returns all YouTube channels available to pull for the current user.
    /// </summary>
    [HttpGet("available")]
    [ProducesResponseType(typeof(IReadOnlyList<ChannelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ChannelDto>>> GetAvailableChannelsAsync(
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var channels = await channelSyncService.GetAvailableYoutubeChannelsForUserAsync(cancellationToken);
        return Ok(channels);
    }

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