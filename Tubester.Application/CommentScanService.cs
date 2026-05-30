using Hangfire;
using Tubester.Abstractions.Channels;
using Tubester.Application.Jobs;

namespace Tubester.Application;

public class CommentScanService(
    IBackgroundJobClient backgroundJobClient,
    ICurrentChannelContext currentChannelContext,
    IChannelRepository channelRepository) : ICommentScanService
{
    public async Task<string?> ScanCommentsAsync(CancellationToken cancellationToken)
    {
        var channelId = currentChannelContext.GetRequiredChannelId();

        var lockAcquired = await channelRepository.TryAcquireCommentScanLockAsync(channelId, cancellationToken);
        if (!lockAcquired)
        {
            return null;
        }

        var jobId = backgroundJobClient.Enqueue<CommentScanJob>(
            j => j.Run(channelId, new CommentScanOptions(), JobCancellationToken.Null));
        return jobId;
    }
}
