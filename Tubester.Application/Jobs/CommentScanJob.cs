using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Replies;
using Tubester.Abstractions.Videos;
using Tubester.Application.Channels;
using Tubester.Application.Common;
using Tubester.Application.Credits;
using Tubester.Domain;
using Tubester.Integration;
using Tubester.Integration.Exceptions;

namespace Tubester.Application.Jobs;

public record CommentScanOptions(bool InitialRun = false);

public sealed partial class CommentScanJob(
    ILogger<CommentScanJob> logger,
    IBackgroundYoutubeIntegration youTubeIntegration,
    IAiClient aiClient,
    IVideoRepository videoRepository,
    IReplyRepository replyRepository,
    IChannelRepository channelRepository,
    IChannelSettingsService channelSettingsRepository,
    ICreditsService creditsService,
    IDateTimeOffsetProvider dateTimeOffsetProvider,
    IEmbeddingServiceFactory embeddingServiceFactory)
{
    private static readonly Regex _nonEmojiRegex = MyRegex();

    [Queue("scanning")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(string channelId, CommentScanOptions? options, IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            { LoggingConstants.ChannelId, channelId }
        });

        try
        {
            var count = await ScanOnceAsync(channelId, options, jobCancellationToken.ShutdownToken);
            logger.LogInformation("Comment scan completed. Drafted: {Count}", count);
        }
        finally
        {
            await channelRepository.ReleaseCommentScanLockAsync(channelId, CancellationToken.None);
        }
    }

    private async Task<int> ScanOnceAsync(string channelId, CommentScanOptions? options, CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetChannelAsync(channelId, cancellationToken);
        if (channel is null)
        {
            logger.LogWarning("Channel not found, skipping scan");
            return 0;
        }

        using var channelScope = logger.BeginScope(new Dictionary<string, object?>
        {
            { LoggingConstants.UploadPlaylistId, channel.UploadsPlaylistId }
        });

        var settings = await channelSettingsRepository.GetOrCreateAsync(channelId, cancellationToken);
        var isInitialRun = options?.InitialRun ?? false;
        var isCommentAssistantEnabled = settings.IsCommentAssistantEnabled;
        if (!isInitialRun && !isCommentAssistantEnabled)
        {
            logger.LogInformation("Skipping comment scan: comment assistant disabled");
            return 0;
        }

        var maxSuggestedRepliesPerSync = settings.MaxSuggestedRepliesPerSync;
        var maxCommentAgeDays = settings.MaxCommentAgeDays;
        var responseForNonTextualComments = settings.ResponseForNonTextualComments ?? "🔥🙌";
        var replyLanguage = settings.ReplyLanguage;

        var userId = channel.UserId;
        var drafted = 0;
        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        foreach (var video in await videoRepository.GetCommentableVideosAsync(channel.UploadsPlaylistId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var videoScope = logger.BeginScope(new Dictionary<string, object?>
            {
                { LoggingConstants.VideoId, video.VideoId }
            });

            if (video.Visibility != VideoVisibility.Public)
            {
                continue;
            }

            if (maxSuggestedRepliesPerSync > 0 && drafted >= maxSuggestedRepliesPerSync)
            {
                logger.LogInformation("Skipping further comments: max suggestions reached ({MaxSuggestions})", maxSuggestedRepliesPerSync);
                break;
            }

            try
            {
                await foreach (var thread in youTubeIntegration.GetUnansweredTopLevelCommentsAsync(
                                   channelId,
                                   video.VideoId,
                                   cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var commentScope = logger.BeginScope(new Dictionary<string, object?>
                    {
                        { LoggingConstants.CommentId, thread.ParentCommentId }
                    });

                    if (maxSuggestedRepliesPerSync > 0 && drafted >= maxSuggestedRepliesPerSync)
                    {
                        logger.LogInformation("Skipping further comments: max suggestions reached ({MaxSuggestions})", maxSuggestedRepliesPerSync);
                        break;
                    }

                    if (maxCommentAgeDays > 0 && thread.PublishedAt.HasValue)
                    {
                        var commentAge = nowUtc - thread.PublishedAt.Value;
                        if (commentAge.TotalDays > maxCommentAgeDays)
                        {
                            logger.LogDebug("Skipping comment: comment not eligible because too old");
                            continue;
                        }
                    }

                    // Try to claim the comment first with Drafting status.
                    // If someone else already claimed it, skip without AI call.
                    var draftingReply = Reply.CreateDrafting(
                        thread.ParentCommentId,
                        thread.VideoId,
                        video.Title ?? string.Empty,
                        thread.Text,
                        dateTimeOffsetProvider.GetUtcNowDateTimeOffset(),
                        thread.PublishedAt ?? DateTimeOffset.MinValue);

                    var claimed = await replyRepository.TryClaimForDraftingAsync(draftingReply, cancellationToken);
                    if (!claimed)
                    {
                        // Already claimed by another process, skip
                        logger.LogDebug("Skipping comment: already claimed by another process");
                        continue;
                    }

                    // Generate and store embedding for the comment when it's first pulled.
                    // This enables the comment to be found in future RAG searches as a successful reply example.
                    await GenerateAndStoreCommentEmbeddingAsync(draftingReply, cancellationToken);

                    string replyText;

                    if (IsEmojiOnly(thread.Text))
                    {
                        replyText = responseForNonTextualComments;
                    }
                    else
                    {
                        string? suggestion;
                        string? idempotencyKey = null;
                        var charged = false;

                        if (!isInitialRun)
                        {
                            idempotencyKey =
                                $"ai-reply-generated:{userId}:{thread.VideoId}:{thread.ParentCommentId}";

                            var spendResult = await creditsService.TrySpendAsync(
                                userId,
                                nameof(CreditActionType.AiReplyGenerated),
                                idempotencyKey,
                                thread.ParentCommentId,
                                new { videoId = thread.VideoId, commentId = thread.ParentCommentId },
                                cancellationToken);

                            if (!spendResult.Succeeded)
                            {
                                logger.LogWarning("Insufficient credits to generate AI reply, skipping");
                                return drafted;
                            }

                            charged = true;
                        }
                        else
                        {
                            logger.LogInformation("Skipping credit charge for initial comment scan");
                        }

                        try
                        {
                            // Search for relevant approved replies for RAG context
                            var relevantExamples = await SearchRelevantRepliesAsync(
                                channelId,
                                thread.Text,
                                cancellationToken);

                            suggestion = await aiClient.SuggestReplyAsync(
                                video.Title ?? string.Empty,
                                thread.Text,
                                replyLanguage,
                                relevantExamples,
                                cancellationToken);
                        }
                        catch
                        {
                            if (charged && idempotencyKey is not null)
                            {
                                var refundIdempotencyKey =
                                    $"ai-reply-generated-refund:{userId}:{thread.VideoId}:{thread.ParentCommentId}";

                                await creditsService.RefundAsync(
                                    userId,
                                    nameof(CreditActionType.AiReplyGenerated),
                                    idempotencyKey,
                                    refundIdempotencyKey,
                                    cancellationToken);
                            }

                            throw;
                        }

                        replyText = string.IsNullOrWhiteSpace(suggestion)
                            ? responseForNonTextualComments
                            : suggestion;
                    }

                    // Update the reply with suggested text (transitions from Drafting to Suggested)
                    draftingReply.SuggestText(replyText, dateTimeOffsetProvider.GetUtcNowDateTimeOffset());
                    await replyRepository.AddOrUpdateReplyAsync(draftingReply, cancellationToken);

                    drafted++;
                }
            }
            catch (CommentsDisabledException ex)
            {
                logger.LogInformation(
                    "Comments are disabled for video {DisabledVideoId}, marking as CommentsAllowed = false",
                    ex.VideoId);

                await videoRepository.MarkCommentsDisabledAsync(channel.UploadsPlaylistId, ex.VideoId, cancellationToken);
            }
        }

        return drafted;
    }

    private static bool IsEmojiOnly(string text)
    {
        return !_nonEmojiRegex.IsMatch(text);
    }

    /// <summary>
    /// Generates and stores an embedding for the comment when it's first pulled.
    /// This embedding enables the comment to be found in future RAG searches as a successful reply example.
    /// </summary>
    private async Task GenerateAndStoreCommentEmbeddingAsync(Reply reply, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reply.CommentText))
        {
            logger.LogDebug(
                "Skipping embedding generation for reply {CommentId}: empty comment text",
                reply.CommentId);
            return;
        }

        try
        {
            var embeddingService = await embeddingServiceFactory.GetServiceAsync(cancellationToken);
            var embeddingResult = await embeddingService.EmbedAsync(reply.CommentText, cancellationToken);
            reply.SetCommentEmbedding(
                embeddingResult.Vector,
                embeddingResult.Model,
                dateTimeOffsetProvider.GetUtcNowDateTimeOffset());

            logger.LogDebug(
                "Generated embedding for reply {CommentId}. Model: {Model}, Dimensions: {Dimensions}",
                reply.CommentId,
                embeddingResult.Model,
                embeddingResult.Vector.ToArray().Length);
        }
        catch (Exception ex)
        {
            // Log but don't fail the job if embedding generation fails
            logger.LogWarning(
                ex,
                "Failed to generate embedding for reply {CommentId}. RAG examples will not include this reply",
                reply.CommentId);
        }
    }

    private async Task<IReadOnlyList<RelevantReplyExample>?> SearchRelevantRepliesAsync(
        string channelId,
        string commentText,
        CancellationToken cancellationToken)
    {
        try
        {
            // Generate embedding for the new comment
            var embeddingService = await embeddingServiceFactory.GetServiceAsync(cancellationToken);
            var embeddingResult = await embeddingService.EmbedAsync(commentText, cancellationToken);

            // Search for relevant approved replies from the same user/channel
            const int maxExamples = 3;
            const double minSimilarityScore = 0.5; // Minimum cosine similarity threshold

            var relevantExamples = await replyRepository.SearchRelevantApprovedRepliesAsync(
                channelId,
                embeddingResult.Vector,
                maxExamples,
                minSimilarityScore,
                cancellationToken);

            if (relevantExamples.Count == 0)
            {
                return null;
            }

            logger.LogDebug(
                "Found {ExampleCount} relevant reply examples for RAG context",
                relevantExamples.Count);

            return relevantExamples;
        }
        catch (Exception ex)
        {
            // Log but don't fail the job if embedding/search fails
            logger.LogWarning(
                ex,
                "Failed to search for relevant replies. Proceeding without RAG context");

            return null;
        }
    }

    [GeneratedRegex(@"\p{L}|\p{N}", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}