using Hangfire;
using Microsoft.Extensions.Logging;

namespace YouTubester.Application.Jobs;

public sealed class AiTemplateJob(
    ILogger<AiTemplateJob> logger,
    IVideoTemplatingService videoTemplatingService)
{
    [Queue("ai-templating")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(
        string userId,
        AiVideoTemplateRequest request,
        IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        try
        {
            await videoTemplatingService.GenerateAiTemplateAsync(userId, request, jobCancellationToken.ShutdownToken);
            logger.LogInformation("AI templating completed for video {TargetVideoId}", request.TargetVideoId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI templating failed for video {TargetVideoId}", request.TargetVideoId);
            throw;
        }
    }
}
