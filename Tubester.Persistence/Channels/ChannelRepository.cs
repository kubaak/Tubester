using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Channels;
using Tubester.Domain;

namespace Tubester.Persistence.Channels;

public sealed class ChannelRepository(TubesterDb db) : IChannelRepository
{
    public async Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        return await db.Set<Channel>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChannelId == channelId, cancellationToken);
    }

    public async Task SetUploadsCutoffAsync(string channelId, DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var channel = await db.Set<Channel>().FirstOrDefaultAsync(c => c.ChannelId == channelId, cancellationToken);
        if (channel is null)
        {
            return;
        }

        if (channel.AdvanceUploadsCutoff(cutoff, DateTimeOffset.UtcNow))
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpsertChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        var existingChannel = await db.Set<Channel>()
            .FirstOrDefaultAsync(c => c.ChannelId == channel.ChannelId, cancellationToken);
        if (existingChannel != null)
        {
            existingChannel.ApplyRemoteSnapshot(channel.Name, channel.UploadsPlaylistId,
                channel.ETag, DateTimeOffset.UtcNow);
            existingChannel.TransferEventsFrom(channel);
        }
        else
        {
            db.Set<Channel>().Add(channel);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryAcquireCommentScanLockAsync(string channelId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var affectedRows = await db.Database.ExecuteSqlInterpolatedAsync(
$"""
UPDATE "Channels"
SET "IsCommentScanRunning" = true, "UpdatedAt" = {now}
WHERE "ChannelId" = {channelId}
AND "IsCommentScanRunning" = false
""", cancellationToken);

        return affectedRows == 1;
    }

    public async Task ReleaseCommentScanLockAsync(string channelId, CancellationToken cancellationToken)
    {
        var channel = await db.Set<Channel>()
            .FirstOrDefaultAsync(c => c.ChannelId == channelId, cancellationToken);

        if (channel is null)
        {
            return;
        }

        channel.CompleteCommentScan(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}
