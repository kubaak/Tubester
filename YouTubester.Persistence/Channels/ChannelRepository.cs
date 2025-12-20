using Microsoft.EntityFrameworkCore;
using YouTubester.Abstractions.Channels;
using YouTubester.Domain;

namespace YouTubester.Persistence.Channels;

public sealed class ChannelRepository(YouTubesterDb db) : IChannelRepository
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
        }
        else
        {
            db.Set<Channel>().Add(channel);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}