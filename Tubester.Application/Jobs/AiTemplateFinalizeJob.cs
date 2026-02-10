using Hangfire;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions.Videos;

namespace Tubester.Application.Jobs;

public sealed class AiTemplateFinalizeJob(
    ILogger<AiTemplateFinalizeJob> logger,
    IVideoRepository videoRepository)
{
    [Queue("ai-templating")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(
        string channelId,
        string videoId,
        IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        try
        {
            await videoRepository.TrySettingAiTemplateInProgressAsync(channelId, videoId, false,
                jobCancellationToken.ShutdownToken);
            logger.LogInformation("AI templating finalized for video {VideoId}", videoId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "AI templating finalization failed for video {VideoId}", videoId);
            throw;
        }
    }
}