using Hangfire;
using Microsoft.Extensions.Logging;
using YouTubester.Application.Contracts.Videos;
using YouTubester.Application.Videos;

namespace YouTubester.Application.Jobs;

public sealed class AiTemplateJob(
    ILogger<AiTemplateJob> logger,
    IAiVideoTemplatingService aiVideoTemplatingService)
{
    [Queue("ai-templating")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(
        string channelId,
        AiVideoTemplateRequest request,
        IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        try
        {
            await aiVideoTemplatingService.GenerateAiTemplateAsync(channelId, request,
                jobCancellationToken.ShutdownToken);
            logger.LogInformation("AI templating completed for video {TargetVideoId}", request.TargetVideoId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI templating failed for video {TargetVideoId}", request.TargetVideoId);
            throw;
        }
    }
}