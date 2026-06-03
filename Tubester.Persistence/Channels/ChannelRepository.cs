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

    /// <summary>
    /// Deletes all channels belonging to a user and their associated settings.
    /// This also cascades to videos, replies, and video playlists through FK constraints.
    /// </summary>
    /// <param name="userId">The user ID whose channels should be deleted.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of channels deleted.</returns>
    public async Task<int> DeleteByUserIdAsync(string userId, CancellationToken cancellationToken)
    {
        var deletedCount = await db.Set<Channel>()
            .Where(c => c.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return deletedCount;
    }

    /// <summary>
    /// Gets all channel IDs for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>List of channel IDs.</returns>
    public async Task<List<string>> GetChannelIdsByUserIdAsync(string userId, CancellationToken cancellationToken)
    {
        return await db.Set<Channel>()
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.ChannelId)
            .ToListAsync(cancellationToken);
    }
}
