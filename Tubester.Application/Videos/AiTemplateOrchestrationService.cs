using Hangfire;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Videos;
using Tubester.Application.Contracts.Videos;
using Tubester.Application.Credits;
using Tubester.Application.Exceptions;
using Tubester.Application.Jobs;

namespace Tubester.Application.Videos;

public class AiTemplateOrchestrationService(
    ICurrentChannelContext channelContext,
    IVideoRepository videoRepository,
    IBackgroundJobClient backgroundJobClient,
    IUserEventLogger userEventLogger,
    ICreditsService creditsService) : IAiTemplateOrchestrationService
{
    public async Task<string> EnqueueAiTemplateAsync(
        string userId,
        AiVideoTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var channelId = channelContext.GetRequiredChannelId();
        var isMarked = await videoRepository.TrySettingAiTemplateInProgressAsync(channelId, request.TargetVideoId, true,
            cancellationToken);

        if (!isMarked)
        {
            throw new AiTemplatingNotStartedException(
                $"Target video {request.TargetVideoId} not found for current channel or an AI template job is already in progress.");
        }

        try
        {
            var aiTemplateEnqueueIdempotencyKey =
                $"ai-template-enqueue:{userId}:{request.TargetVideoId}";

            var aiTemplateEnqueueSpendSucceeded = await creditsService.TrySpendAsync(
                userId,
                CreditActionType.AiTemplateEnqueued.ToString(),
                aiTemplateEnqueueIdempotencyKey,
                request.TargetVideoId,
                new
                {
                    generateTitle = request.GenerateTitle,
                    generateDescription = request.GenerateDescription,
                    generateTags = request.GenerateTags
                },
                cancellationToken);

            if (!aiTemplateEnqueueSpendSucceeded)
            {
                throw new Common.ForbiddenException(
                    "Insufficient credits to enqueue AI templating.");
            }

            var jobId = backgroundJobClient.Enqueue<AiTemplateJob>(
                job => job.Run(channelId, request, JobCancellationToken.Null));

            await userEventLogger.LogAsync(
                userId,
                UserEventType.AiTemplateEnqueued,
                request.TargetVideoId,
                null,
                new
                {
                    generateTitle = request.GenerateTitle,
                    generateDescription = request.GenerateDescription,
                    generateTags = request.GenerateTags
                },
                cancellationToken);

            backgroundJobClient.ContinueJobWith<AiTemplateFinalizeJob>(
                jobId,
                job => job.Run(channelId, request.TargetVideoId, JobCancellationToken.Null),
                JobContinuationOptions.OnAnyFinishedState);

            return jobId;
        }
        catch
        {
            await videoRepository.TrySettingAiTemplateInProgressAsync(channelId, request.TargetVideoId, false,
                cancellationToken);
            throw;
        }
    }
}