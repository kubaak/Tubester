using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Abstractions.Account;
using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Videos;
using Tubester.Application.Exceptions;
using Tubester.Application.Videos;
using Tubester.Domain;

namespace Tubester.Api;

/// <summary>
/// 
/// </summary>
/// <param name="videoService"></param>
/// <param name="aiTemplateOrchestrationService"></param>
[ApiController]
[Route("api/videos")]
[Tags("Videos")]
[Authorize]
public sealed class VideosController(
    IVideoService videoService,
    IAiTemplateOrchestrationService aiTemplateOrchestrationService,
    ICurrentUserContext currentUserContext
) : ApiControllerBase
{
    /// <summary>
    /// Copies video template metadata from source to target video using cached data.
    /// </summary>
    /// <param name="request">Request containing source and target video IDs.</param>
    /// <param name="ct"></param>
    /// <returns>Job ID for the background operation.</returns>
    [HttpPost("copy-template")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CopyTemplate(
        [FromBody] CopyVideoTemplateRequest request, CancellationToken ct = default)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(request.SourceVideoId))
        {
            return BadRequest(new { error = "SourceVideoId is required and cannot be empty." });
        }

        if (string.IsNullOrWhiteSpace(request.TargetVideoId))
        {
            return BadRequest(new { error = "TargetVideoId is required and cannot be empty." });
        }

        if (string.Equals(request.SourceVideoId.Trim(), request.TargetVideoId.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "SourceVideoId and TargetVideoId must be different." });
        }

        var userId = currentUserContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var result = await videoService.CopyTemplateAsync(userId, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Improve video metadata using AI
    /// </summary>
    /// <param name="request"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpPost("ai-template")]
    [ProducesResponseType(typeof(AiTemplateEnqueueResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AiTemplate(
        [FromBody] AiVideoTemplateRequest request,
        CancellationToken ct = default)
    {
        var operationId = Request.Headers["OperationId"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(operationId))
        {
            return BadRequest("Missing OperationId header.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetVideoId))
        {
            return BadRequest(new { error = "TargetVideoId is required and cannot be empty." });
        }

        if (string.IsNullOrWhiteSpace(request.PromptEnrichment))
        {
            return BadRequest(new { error = "PromptEnrichment is required and cannot be empty." });
        }

        var userId = currentUserContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var result = await aiTemplateOrchestrationService.EnqueueAiTemplateAsync(userId, operationId, request, ct);
            return Ok(new AiTemplateEnqueueResult(result));
        }
        catch (AiTemplatingNotStartedException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    /// <summary>
    /// Gets a paginated list of videos with optional title and visibility filters.
    /// </summary>
    /// <param name="request">Filter parameters for video listing.</param>
    /// <param name="ct"></param>
    /// <returns>Paginated list of videos and next-page token if available.</returns>
    [HttpPost("search")]
    [ProducesResponseType(typeof(PagedResult<VideoListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<VideoListItemDto>>> GetVideos(
        [FromBody] GetVideosRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await videoService.GetVideosAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidPageSizeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidPageTokenException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets video details (title, description, tags) for a single video.
    /// </summary>
    /// <param name="videoId">YouTube video ID.</param>
    /// <param name="ct"></param>
    [HttpGet("{videoId}")]
    [ProducesResponseType(typeof(VideoDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VideoDetailsDto>> GetVideo(string videoId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return BadRequest(new { error = "VideoId is required and cannot be empty." });
        }

        var videoDetails = await videoService.GetVideoDetailsAsync(videoId, ct);

        if (videoDetails is null)
        {
            return NotFound();
        }

        return Ok(videoDetails);
    }

    /// <summary>
    /// Updates a video's editable metadata (title, description, tags).
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("update")]
    [Authorize(Policy = "RequiresYouTubeWrite")]
    [ProducesResponseType(typeof(VideoDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VideoDetailsDto>> UpdateVideo(
        [FromBody] UpdateVideoMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        var operationId = Request.Headers["OperationId"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(operationId))
        {
            return BadRequest("Missing OperationId header.");
        }

        if (string.IsNullOrWhiteSpace(request.VideoId))
        {
            return BadRequest(new { error = "VideoId is required and cannot be empty." });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { error = "Title is required and cannot be empty." });
        }

        var userId = currentUserContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var updatedVideoDetails = await videoService.UpdateVideoMetadataAsync(userId, operationId, request, cancellationToken);

        if (updatedVideoDetails is null)
        {
            return NotFound();
        }

        return Ok(updatedVideoDetails);
    }

    /// <summary>
    /// Saves draft video metadata (title, description, tags) without submitting to YouTube.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("save-draft")]
    [ProducesResponseType(typeof(VideoDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VideoDetailsDto>> SaveDraft(
        [FromBody] UpdateVideoMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.VideoId))
        {
            return BadRequest(new { error = "VideoId is required and cannot be empty." });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { error = "Title is required and cannot be empty." });
        }

        var userId = currentUserContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var savedVideoDetails = await videoService.SaveDraftMetadataAsync(userId, request, cancellationToken);

        if (savedVideoDetails is null)
        {
            return NotFound();
        }

        return Ok(savedVideoDetails);
    }

    /// <summary>
    /// Resyncs a video's details and playlists from YouTube.
    /// </summary>
    /// <param name="videoId">YouTube video ID.</param>
    /// <param name="ct"></param>
    /// <returns>Updated video details with current playlists.</returns>
    [HttpPost("resync")]
    [ProducesResponseType(typeof(VideoDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VideoDetailsDto>> ResyncVideo(
        string videoId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return BadRequest(new { error = "VideoId is required and cannot be empty." });
        }

        var result = await videoService.ResyncVideoAsync(videoId, ct);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }
}
