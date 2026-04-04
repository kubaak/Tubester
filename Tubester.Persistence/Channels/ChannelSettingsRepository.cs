using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Channels;
using Tubester.Domain;

namespace Tubester.Persistence.Channels;

public sealed class ChannelSettingsRepository(TubesterDb db) : IChannelSettingsRepository
{
    public async Task<ChannelSettings?> GetByChannelIdAsync(string channelId, CancellationToken cancellationToken)
    {
        return await db.Set<ChannelSettings>()
            .FirstOrDefaultAsync(channelSettings => channelSettings.ChannelId == channelId, cancellationToken);
    }

    public async Task UpsertAsync(ChannelSettings settings, CancellationToken cancellationToken)
    {
        var existing = await db.Set<ChannelSettings>()
            .FirstOrDefaultAsync(channelSettings => channelSettings.ChannelId == settings.ChannelId, cancellationToken);

        if (existing is null)
        {
            db.Set<ChannelSettings>().Add(settings);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
