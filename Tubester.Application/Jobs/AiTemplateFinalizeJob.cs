using Hangfire;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions.Videos;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;

namespace Tubester.Application.Jobs;

public sealed class AiTemplateFinalizeJob(
    ILogger<AiTemplateFinalizeJob> logger,
    IVideoRepository videoRepository)
{
    [Queue("ai-templating")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(
        string channelId,
        AiVideoTemplateRequest request,
        IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        var operations = request.GetRequestedAiOperations();

        if (operations == AiVideoOperationFlags.None)
        {
            logger.LogInformation(
                "AI templating finalization skipped for video {VideoId} because no metadata operations were requested",
                request.TargetVideoId);

            return;
        }

        try
        {
            await videoRepository.TryClearAiOperationsInProgressAsync(
                channelId,
                request.TargetVideoId,
                operations,
                jobCancellationToken.ShutdownToken);

            logger.LogInformation(
                "AI templating finalized for video {VideoId}. Cleared AI operations: {AiOperations}",
                request.TargetVideoId,
                operations);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "AI templating finalization failed for video {VideoId}. AI operations: {AiOperations}",
                request.TargetVideoId,
                operations);

            throw;
        }
    }
}