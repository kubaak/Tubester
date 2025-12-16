using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.Extensions.Logging;
using YouTubester.Abstractions.Replies;
using YouTubester.Abstractions.Videos;
using YouTubester.Domain;
using YouTubester.Integration;

namespace YouTubester.Application.Jobs;

public sealed class CommentScanJob(
    ILogger<CommentScanJob> logger,
    IBackgroundYoutubeIntegration youTubeIntegration,
    IAiClient aiClient,
    IVideoRepository videoRepository,
    IReplyRepository replyRepository)
{
    [Queue("scanning")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(string channelId, IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        try
        {
            var count = await ScanOnceAsync(channelId, jobCancellationToken.ShutdownToken);
            logger.LogInformation("Comment scan completed. Drafted: {Count}", count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Comment scan failed");
            throw;
        }
    }

    private static bool IsEmojiOnly(string text)
    {
        return !_nonEmojiRegex.IsMatch(text);
    }

    private async Task<int> ScanOnceAsync(string channelId, CancellationToken cancellationToken)
    {
        var drafted = 0;

        foreach (var video in await videoRepository.GetCommentableVideosAsync(channelId, cancellationToken))
        {
            if (video.Visibility != VideoVisibility.Public)
            {
                continue;
            }

            await foreach (var thread in youTubeIntegration.GetUnansweredTopLevelCommentsAsync(
                               channelId, video.VideoId, cancellationToken))
            {
                var existingReply = await replyRepository.GetReplyAsync(thread.ParentCommentId, cancellationToken);
                // Skip if we've already pulled this
                if (existingReply is not null)
                {
                    continue;
                }

                string replyText;
                if (IsEmojiOnly(thread.Text))
                {
                    replyText = "🔥🙌";
                }
                else
                {
                    var suggestion = await aiClient.SuggestReplyAsync(
                        video.Title ?? string.Empty,
                        video.Tags,
                        thread.Text,
                        cancellationToken);

                    replyText = string.IsNullOrWhiteSpace(suggestion)
                        ? "Thanks for the comment! 🙌"
                        : suggestion;
                }

                var reply = Reply.Create(thread.ParentCommentId, thread.VideoId, video.Title, thread.Text,
                    DateTimeOffset.UtcNow);
                reply.SuggestText(replyText, DateTimeOffset.UtcNow);

                await replyRepository.AddOrUpdateReplyAsync(reply, cancellationToken);

                drafted++;
            }
        }


        return drafted;
    }

    private static readonly Regex _nonEmojiRegex = new(@"\p{L}|\p{N}", RegexOptions.Compiled);
}