using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using YouTubester.Abstractions.Channels;
using YouTubester.Abstractions.Replies;
using YouTubester.Abstractions.Videos;
using YouTubester.Domain;
using YouTubester.Integration;

namespace YouTubester.Worker;

public partial class CommentScanWorker(
    ILogger<CommentScanWorker> log,
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> opt)
    : BackgroundService
{
    private readonly WorkerOptions _opt = opt.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("CommentScanWorker started. Interval: {S}s", _opt.IntervalSeconds);
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(_opt.IntervalSeconds));

        do
        {
            try
            {
                var count = await ScanOnceAsync(stoppingToken);
                log.LogInformation("Scan completed. Drafted: {Count}", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Scan failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static bool IsEmojiOnly(string text)
    {
        return !_nonEmojiRegex.IsMatch(text);
    }

    private async Task<int> ScanOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var yt = scope.ServiceProvider.GetRequiredService<IYouTubeIntegration>();
        var ai = scope.ServiceProvider.GetRequiredService<IAiClient>();
        var channelRepository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var videoRepository = scope.ServiceProvider.GetRequiredService<IVideoRepository>();
        var replyRepository = scope.ServiceProvider.GetRequiredService<IReplyRepository>();

        var channels = await channelRepository.GetChannelsAsync(cancellationToken);
        var drafted = 0;
        foreach (var channel in channels)
        {
            var channelId = channel.ChannelId;
            var userId = channel.UserId;

            foreach (var video in await videoRepository.GetCommentableVideosAsync(cancellationToken))
            {
                if (drafted >= _opt.MaxDraftsPerRun)
                {
                    break;
                }

                //todo eliminate separate call and set the property if GetUnansweredTopLevelCommentsAsync throws an exception
                if (video.CommentsAllowed is null)
                {
                    var isCommentAllowed = await yt.CheckCommentsAllowedAsync(video.VideoId,
                        cancellationToken);
                    if (isCommentAllowed.HasValue)
                    {
                        video.SetCommentsAllowed(isCommentAllowed.Value);
                        //todo batch
                        await videoRepository.UpsertAsync([video], cancellationToken);
                    }
                }


                if (video.Visibility != VideoVisibility.Public)
                {
                    continue;
                }

                await foreach (var thread in yt.GetUnansweredTopLevelCommentsAsync(channelId,
                                   video.VideoId, cancellationToken))
                {
                    if (drafted >= _opt.MaxDraftsPerRun)
                    {
                        break;
                    }

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
                        var suggestion = await ai.SuggestReplyAsync(
                            video.Title ?? string.Empty,
                            video.Tags,
                            thread.Text,
                            cancellationToken);

                        replyText = string.IsNullOrWhiteSpace(suggestion)
                            ? "Thanks for the comment! 🙌"
                            : suggestion;
                    }

                    var reply = Reply.Create(thread.ParentCommentId, thread.VideoId, video.Title, thread.Text,
                        DateTimeOffset.Now);
                    reply.SuggestText(replyText, DateTimeOffset.Now);

                    await replyRepository.AddOrUpdateReplyAsync(reply, cancellationToken);

                    drafted++;
                }
            }
        }


        return drafted;
    }


    private static readonly Regex _nonEmojiRegex = new(@"\p{L}|\p{N}", RegexOptions.Compiled);
}