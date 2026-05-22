using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Abstractions.Channels;
using Tubester.Application.Contracts.Videos;
using Tubester.Application.Playlists;

namespace Tubester.Api;

/// <summary>Playlists</summary>
[ApiController]
[Route("api/playlists")]
[Tags("Playlists")]
[Authorize]
public sealed class PlayListsController(
    IPlaylistService playlistService,
    ICurrentChannelContext currentChannelContext)
    : ApiControllerBase
{
    /// <summary>
    /// Returns playlists for the current channel.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlaylistDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<PlaylistDto>>> ListEndpoint(
        CancellationToken cancellationToken)
    {
        var channelId = currentChannelContext.ChannelId;
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return Unauthorized();
        }

        var playlists = await playlistService.ListAsync(channelId, cancellationToken);
        return Ok(playlists);
    }
}
