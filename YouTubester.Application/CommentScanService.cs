using Hangfire;
using YouTubester.Abstractions.Channels;
using YouTubester.Application.Jobs;

namespace YouTubester.Application;

public class CommentScanService(
    IBackgroundJobClient backgroundJobClient,
    ICurrentChannelContext currentChannelContext) : ICommentScanService
{
    public string ScanCommentsAsync(CancellationToken cancellationToken)
    {
        var channelId = currentChannelContext.GetRequiredChannelId();
        var res = backgroundJobClient.Enqueue<CommentScanJob>(
            j => j.Run(channelId, JobCancellationToken.Null));
        return res;
    }
}