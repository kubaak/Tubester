using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YouTubester.Application.Contracts;
using YouTubester.Application.Contracts.Videos;
using YouTubester.Application.Exceptions;
using YouTubester.Application.Videos;
using YouTubester.Domain;

namespace YouTubester.Api;

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
    IAiTemplateOrchestrationService aiTemplateOrchestrationService
) : ControllerBase
{
    /// <summary>
    /// Copies video template metadata from source to target video using cached data.
    /// </summary>
    /// <param name="request">Request containing source and target video IDs.</param>
    /// <param name="ct"></param>
    /// <returns>Job ID for the background operation.</returns>
    [HttpPost("copy-template")]
    [Authorize(Policy = "RequiresYouTubeWrite")]
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

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var result = await videoService.CopyTemplateAsync(userId, request, ct);
        return Ok(result);
    }

    [HttpPost("ai-template")]
    [ProducesResponseType(typeof(AiTemplateEnqueueResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AiTemplate(
        [FromBody] AiVideoTemplateRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TargetVideoId))
        {
            return BadRequest(new { error = "TargetVideoId is required and cannot be empty." });
        }

        if (string.IsNullOrWhiteSpace(request.PromptEnrichment))
        {
            return BadRequest(new { error = "PromptEnrichment is required and cannot be empty." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var result = await aiTemplateOrchestrationService.EnqueueAiTemplateAsync(userId, request, ct);
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
    /// <param name="title">Case-insensitive substring filter for video titles.</param>
    /// <param name="visibility">
    /// Optional visibility filter. Multiple values allowed 
    /// (<c>?visibility=Public&amp;visibility=Unlisted</c>). 
    /// Accepts enum names or numeric values (Public=0, Unlisted=1, Private=2, Scheduled=3).
    /// </param>
    /// <param name="pageSize">Items per page (1–100, default 30).</param>
    /// <param name="pageToken">Cursor token for pagination, or <c>null</c> for first page.</param>
    /// <param name="ct"></param>
    /// <returns>Paginated list of videos and next-page token if available.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<VideoListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<VideoListItemDto>>> GetVideos(
        [FromQuery] string? title,
        [FromQuery(Name = "visibility")] VideoVisibility[]? visibility,
        [FromQuery] int? pageSize,
        [FromQuery] string? pageToken,
        CancellationToken ct)
    {
        try
        {
            var result = await videoService.GetVideosAsync(title, visibility, pageSize, pageToken, ct);
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
    /// Gets video metadata (title, description, tags) for a single video.
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
        if (string.IsNullOrWhiteSpace(request.VideoId))
        {
            return BadRequest(new { error = "VideoId is required and cannot be empty." });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { error = "Title is required and cannot be empty." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var updatedVideoDetails = await videoService.UpdateVideoMetadataAsync(userId, request, cancellationToken);

        if (updatedVideoDetails is null)
        {
            return NotFound();
        }

        return Ok(updatedVideoDetails);
    }
}