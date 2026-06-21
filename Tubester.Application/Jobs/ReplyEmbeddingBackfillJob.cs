using Hangfire;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions;
using Tubester.Abstractions.Replies;
using Tubester.Application.Common;

namespace Tubester.Application.Jobs;

public sealed class ReplyEmbeddingBackfillJob(
    IReplyRepository replyRepository,
    IEmbeddingServiceFactory embeddingServiceFactory,
    IDateTimeOffsetProvider dateTimeOffsetProvider,
    IBackgroundJobClient backgroundJobClient,
    ILogger<ReplyEmbeddingBackfillJob> logger)
{
    private const int DefaultBatchSize = 100;
    private const int DefaultDelaySeconds = 60;
    private const int MinBatchSize = 1;
    private const int MaxBatchSize = 500;
    private const int MinDelaySeconds = 1;
    private const int MaxDelaySeconds = 3600;

    [AutomaticRetry(Attempts = 0)]
    [Queue("embeddings")]
    public async Task RunAsync(
        string uploadPlaylistId,
        int maxComments,
        int batchSize = DefaultBatchSize,
        int delaySeconds = DefaultDelaySeconds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uploadPlaylistId))
        {
            throw new ArgumentException("Upload playlist ID is required.", nameof(uploadPlaylistId));
        }

        if (maxComments <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxComments), "Max comments must be greater than zero.");
        }

        if (batchSize is < MinBatchSize or > MaxBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        if (delaySeconds is < MinDelaySeconds or > MaxDelaySeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            { LoggingConstants.UploadPlaylistId, uploadPlaylistId }
        });

        var startedAt = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
        var embedded = 0;
        var skipped = 0;
        var take = Math.Min(batchSize, maxComments);

        var embeddingService = await embeddingServiceFactory.GetServiceAsync(cancellationToken);

        var replies = await replyRepository.GetRepliesMissingCommentEmbeddingAsync(
            uploadPlaylistId,
            take,
            cancellationToken);

        foreach (var reply in replies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reply.CommentEmbedding is not null ||
                string.IsNullOrWhiteSpace(reply.CommentText) ||
                string.IsNullOrWhiteSpace(reply.FinalText))
            {
                skipped++;
                continue;
            }

            var result = await embeddingService.EmbedAsync(reply.CommentText, cancellationToken);

            reply.SetCommentEmbedding(
                result.Vector,
                result.Model,
                dateTimeOffsetProvider.GetUtcNowDateTimeOffset());

            await replyRepository.SaveChangesAsync(cancellationToken);

            embedded++;
        }

        var remainingMaxComments = maxComments - replies.Count;
        var shouldScheduleNext = replies.Count == take && remainingMaxComments > 0;

        if (shouldScheduleNext)
        {
            backgroundJobClient.Schedule<ReplyEmbeddingBackfillJob>(
                job => job.RunAsync(
                    uploadPlaylistId,
                    remainingMaxComments,
                    batchSize,
                    delaySeconds,
                    CancellationToken.None),
                TimeSpan.FromSeconds(delaySeconds));
        }

        var duration = dateTimeOffsetProvider.GetUtcNowDateTimeOffset() - startedAt;

        logger.LogInformation(
            "Reply embedding backfill batch completed. Embedded={Embedded}, Skipped={Skipped}, Retrieved={Retrieved}, RemainingMaxComments={RemainingMaxComments}, ScheduledNext={ScheduledNext}, Duration={Duration}",
            embedded,
            skipped,
            replies.Count,
            remainingMaxComments,
            shouldScheduleNext,
            duration);
    }
}