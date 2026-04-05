using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Application;

namespace Tubester.Api;

/// <summary>Channels</summary>
[ApiController]
[Route("api/coments")]
[Tags("comments")]
[Authorize]
public class CommentsController(ICommentScanService commentScanService) : ApiControllerBase
{
    /// <summary>
    /// Registers the gob to pull the comments
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("pull")]
    [ProducesResponseType(typeof(ActionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Pull(
        CancellationToken cancellationToken)
    {
        var result = await commentScanService.ScanCommentsAsync(cancellationToken);
        if (result is null)
        {
            return Conflict("A comment scan is already running for this channel.");
        }

        return Ok(result);
    }
}