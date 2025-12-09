using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YouTubester.Application;
using YouTubester.Application.Contracts.Replies;
using YouTubester.Domain;

namespace YouTubester.Api;

[ApiController]
[Route("api/replies")]
[Tags("Replies")]
[Authorize]
public class RepliesController(IReplyService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Reply>>> GetDrafts(CancellationToken cancellationToken = default)
    {
        return Ok(await service.GetRepliesForApprovalAsync(cancellationToken));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDraft([FromRoute] string id, CancellationToken cancellationToken)
    {
        var reply = await service.DeleteAsync(id, cancellationToken);
        if (reply is null)
        {
            return NotFound();
        }

        return Ok(reply);
    }

    /// <summary>
    /// Approves batch of replies
    /// </summary>
    /// <param name="decisions"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("approve")]
    [Authorize(Policy = "RequiresYouTubeWrite")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BatchDecisionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchDecisionResultDto>> BatchApprove(
        [FromBody] DraftDecisionDto[] decisions,
        CancellationToken cancellationToken)
    {
        if (decisions.Length == 0)
        {
            return BadRequest("Missing decisions.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var result = await service.ApplyBatchAsync(userId, decisions, cancellationToken);
        return Ok(result);
    }

    [HttpPost("batch-ignore")]
    [ProducesResponseType(typeof(BatchIgnoreResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchIgnoreResult>> BatchIgnore(
        [FromBody] string[] commentIds,
        CancellationToken ct)
    {
        if (commentIds.Length == 0)
        {
            return BadRequest(new { error = "CommentIds cannot be empty." });
        }

        var result = await service.IgnoreBatchAsync(commentIds, ct);
        return Ok(result);
    }
}