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
        string operationId,
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
            string keyBase;
            string creditAction;
            if (request.SuggestPlaylists)
            {
                keyBase = "ai-template-playlist-enqueue";
                creditAction = nameof(CreditActionType.AiTemplateWithPlaylistEnqueued);
            }
            else
            {
                keyBase = "ai-template-enqueue";
                creditAction = nameof(CreditActionType.AiTemplateEnqueued);
            }
            var aiTemplateEnqueueIdempotencyKey =
                $"{keyBase}:{userId}:{request.TargetVideoId}:{operationId}";

            var aiTemplateEnqueueSpendSucceeded = await creditsService.TrySpendAsync(
                userId,
                creditAction,
                aiTemplateEnqueueIdempotencyKey,
                request.TargetVideoId,
                new
                {
                    generateTitle = request.GenerateTitle,
                    generateDescription = request.GenerateDescription,
                    generateTags = request.GenerateTags,
                    suggestPlaylists = request.SuggestPlaylists
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

            if (request.SuggestPlaylists)
            {
                backgroundJobClient.Enqueue<AiPlaylistSuggestionJob>(
                    job => job.Run(channelId, request.TargetVideoId, request.PromptEnrichment, JobCancellationToken.Null));
            }

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