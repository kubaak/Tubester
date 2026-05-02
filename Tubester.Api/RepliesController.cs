using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Application;
using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Replies;
using Tubester.Domain;

namespace Tubester.Api;

/// <summary>
/// 
/// </summary>
/// <param name="service"></param>
[ApiController]
[Route("api/replies")]
[Tags("Replies")]
[Authorize]
public class RepliesController(IReplyService service) : ApiControllerBase
{
    /// <summary>
    /// Searches suggested replies for the replies page.
    /// </summary>
    /// <param name="request">
    /// Search criteria for suggested replies, including optional video ID filter,
    /// original comment text filter, page size, and pagination token.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paginated list of suggested replies and a next-page token if more results are available.</returns>
    [HttpPost("suggested/search")]
    [ProducesResponseType(typeof(PagedResult<ReplyListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ReplyListItemDto>>> SearchSuggestedReplies(
        [FromBody] SearchSuggestedRepliesRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await service.GetRepliesAsync(
                [ReplyStatus.Suggested],
                request.VideoId is null ? null : [request.VideoId],
                request.OriginalComment,
                request.PageSize,
                request.PageToken,
                ct);

            return Ok(result);
        }
        catch (Application.Exceptions.InvalidPageSizeException ex)
        {
            return BadRequest(new ApiErrorDto(ex.Message));
        }
        catch (Application.Exceptions.InvalidPageTokenException ex)
        {
            return BadRequest(new ApiErrorDto(ex.Message));
        }
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
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("approve")]
    [Authorize(Policy = "RequiresYouTubeWrite")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BatchDecisionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchDecisionResultDto>> BatchApprove(
        [FromBody] BatchDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var operationId = Request.Headers["OperationId"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(operationId))
        {
            return BadRequest("Missing OperationId header.");
        }

        if (request.Decisions.Length == 0)
        {
            return BadRequest("Missing decisions.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var result = await service.ApplyBatchAsync(userId, operationId, request.Decisions, cancellationToken);
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
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="Error"></param>
    public sealed record ApiErrorDto(string Error);
}