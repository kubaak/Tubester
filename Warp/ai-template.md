You are helping me extend the Tubester API with a separate AI templating endpoint.

## Current state

- API project: Tubester.Api
- Worker: Tubester.Worker (uses Hangfire)
- Existing queues: "scanning", "default"
- There is an existing endpoint:

  ```csharp
  /// <summary>
  /// Copies video template metadata from source to target video using cached data.
  /// </summary>
  /// <param name="request">Request containing source and target video IDs.</param>
  /// <param name="ct"></param>
  /// <returns>Result of synchronous copy.</returns>
  [HttpPost("copy-template")]
  [Authorize(Policy = "RequiresYouTubeWrite")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> CopyTemplate(
      [FromBody] CopyVideoTemplateRequest request, CancellationToken ct = default)
  {
      if (string.IsNullOrWhiteSpace(request.SourceVideoId))
          return BadRequest(new { error = "SourceVideoId is required and cannot be empty." });

      if (string.IsNullOrWhiteSpace(request.TargetVideoId))
          return BadRequest(new { error = "TargetVideoId is required and cannot be empty." });

      if (string.Equals(request.SourceVideoId.Trim(), request.TargetVideoId.Trim(),
              StringComparison.OrdinalIgnoreCase))
      {
          return BadRequest(new { error = "SourceVideoId and TargetVideoId must be different." });
      }

      var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
      if (string.IsNullOrWhiteSpace(userId))
          return Unauthorized();

      var result = await videoTemplatingService.CopyTemplateAsync(userId, request, ct);
      return Ok(result);
  }

Current request DTO (will be changed):

csharp
Copy code
public sealed record CopyVideoTemplateRequest(
string SourceVideoId,
string TargetVideoId,
bool CopyTags = true,
bool CopyLocation = true,
bool CopyPlaylists = true,
bool CopyCategory = true,
bool CopyDefaultLanguages = true,
AiSuggestionOptions? AiSuggestionOptions = null
);

public sealed record AiSuggestionOptions(
string PromptEnrichment,
bool GenerateTitle = true,
bool GenerateDescription = true,
bool GenerateTags = true
);
/api/videos/copy-template is synchronous and must stay synchronous, and it must not enqueue any jobs.

There is an ICurrentChannelContext used elsewhere to get the current channelId for the logged-in user:

csharp
Copy code
public interface ICurrentChannelContext
{
string GetRequiredChannelId();
}
TubesterDb has Channels and Videos tables, with a relation Channel.Id → Video.ChannelId (or equivalent).

New requirements
We want to introduce a separate AI templating endpoint and an associated Hangfire job.

1. Plain Copy Template stays as-is (but without AI)
   Endpoint: POST /api/videos/copy-template

Behavior:

Synchronous.

Just copies metadata from source video to target video.

No AI at all.

Changes:

Remove AiSuggestionOptions from CopyVideoTemplateRequest entirely.

The action should continue to call videoTemplatingService.CopyTemplateAsync(...) synchronously, as it does now.

The response type / shape should remain the same as currently.

2. New AI Template endpoint
   Endpoint: POST /api/videos/ai-template

Action name: AiTemplate.

Behavior:

Synchronous HTTP response, but it will enqueue a background job which runs asynchronously via the worker.

The endpoint should:

Validate the request.

Validate that the target video belongs to the currently selected channel.

Enqueue a Hangfire job on a new queue called "ai-templating".

Return a small response with the Hangfire JobId (e.g. { jobId: "..." }).

3. AI request DTO
   Create a dedicated DTO for the AI endpoint:

csharp
Copy code
public sealed record AiVideoTemplateRequest(
string TargetVideoId,
string PromptEnrichment,
bool GenerateTitle = true,
bool GenerateDescription = true,
bool GenerateTags = true
);
No SourceVideoId here.

This template is AI-only for a single target video.

4. Validation for AI endpoint
   The AiTemplate action must:

Get userId from ClaimsPrincipal:

If missing → return Unauthorized().

Validate AiVideoTemplateRequest:

TargetVideoId is not null/empty.

PromptEnrichment is not null/empty.

Validate that the TargetVideoId belongs to the current user’s channel:

Use ICurrentChannelContext.GetRequiredChannelId() to get channelId.

In the API project, before enqueueing, query TubesterDb to ensure:

csharp
Copy code
var channelId = currentChannelContext.GetRequiredChannelId();
var exists = await dbContext.Videos
.Where(v => v.Id == request.TargetVideoId && v.ChannelId == channelId)
.AnyAsync(ct);
If no such video exists → return BadRequest (or NotFound) with a simple { error = "Target video not found for current
channel." }.

If everything is valid:

Enqueue a Hangfire job using a new AiTemplateJob class.

Use a dedicated queue named "ai-templating".

5. Hangfire job: AiTemplateJob
   Create a new job class in the Worker project, e.g. Tubester.Application.Jobs.AiTemplateJob:

csharp
Copy code
public sealed class AiTemplateJob
{
private readonly IVideoTemplatingService videoTemplatingService;

    public AiTemplateJob(IVideoTemplatingService videoTemplatingService)
    {
        this.videoTemplatingService = videoTemplatingService;
    }

    public async Task Run(string userId, AiVideoTemplateRequest request, IJobCancellationToken cancellationToken)
    {
        // This method will:
        // - Re-validate or load the target video (if needed).
        // - Call the AI client via IVideoTemplatingService to generate title/description/tags.
        // - Persist the updated template into the DB.
        // No enqueueing here; this IS the job.
    }

}
The job does the real AI work; no further enqueueing inside.

6. Service interface changes
   Currently we have something like:

csharp
Copy code
public interface IVideoTemplatingService
{
Task<CopyTemplateResult> CopyTemplateAsync(string userId, CopyVideoTemplateRequest request, CancellationToken ct);
}
Extend it with a new method for the AI job:

csharp
Copy code
public interface IVideoTemplatingService
{
Task<CopyTemplateResult> CopyTemplateAsync(
string userId,
CopyVideoTemplateRequest request,
CancellationToken ct);

    Task GenerateAiTemplateAsync(
        string userId,
        AiVideoTemplateRequest request,
        CancellationToken ct);

}
GenerateAiTemplateAsync will be called from AiTemplateJob.Run(...).

It should:

Load the target video (optional: verify channel again).

Call the AI client with PromptEnrichment and flags.

Apply generated metadata and save changes.

7. Enqueueing the job
   In the API controller, inject IBackgroundJobClient and enqueue:

csharp
Copy code
var jobId = backgroundJobClient.Enqueue<AiTemplateJob>(
job => job.Run(userId, request, JobCancellationToken.Null));
But ensure that it uses the "ai-templating" queue:

Either:

Configure the job with an attribute, e.g. [Queue("ai-templating")] on AiTemplateJob.Run, or

Use Hangfire’s Enqueue overload that lets you specify queue, or

Configure in AddHangfireServer to include "ai-templating" along with existing queues.

In AddWorkerCore (Worker project), update Hangfire server options to also listen on "ai-templating":

csharp
Copy code
services.AddHangfireServer(o => o.Queues = new[] { "scanning", "ai-templating", "default" });

8. Copy endpoint DTO cleanup
   Update CopyVideoTemplateRequest to:

csharp
Copy code
public sealed record CopyVideoTemplateRequest(
string SourceVideoId,
string TargetVideoId,
bool CopyTags = true,
bool CopyLocation = true,
bool CopyPlaylists = true,
bool CopyCategory = true,
bool CopyDefaultLanguages = true
);
Remove AiSuggestionOptions entirely (and delete the type if not used anywhere else).

9. Response from AI endpoint
   AiTemplate should return a small DTO with the job id, e.g.:

csharp
Copy code
public sealed record AiTemplateEnqueueResult(string JobId);
HTTP 200 body: { "jobId": "xxxx" }.

On validation errors: 400 with { "error": "..." } similar to the existing style.

Tasks
Update CopyVideoTemplateRequest to remove AiSuggestionOptions and keep /api/videos/copy-template synchronous, unchanged
behavior (no enqueue).

Add AiVideoTemplateRequest DTO for the new AI endpoint.

Add AiTemplateEnqueueResult DTO.

Add AiTemplate action to the Videos controller:

Validates request (fields + ownership via ICurrentChannelContext + DB).

Gets userId from claims.

Enqueues AiTemplateJob on "ai-templating" queue.

Returns 200 with job id.

Create AiTemplateJob in Worker project and wire it into DI.

Extend IVideoTemplatingService with GenerateAiTemplateAsync and implement it.

Update AddWorkerCore Hangfire server config to listen also on "ai-templating" queue.

Please generate:

The updated request/response DTOs.

The new AiTemplate controller action.

The AiTemplateJob skeleton.

The updated IVideoTemplatingService interface + basic implementation changes.

The updated Hangfire server queue configuration.