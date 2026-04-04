using Tubester.Domain;

namespace Tubester.Abstractions.Channels;

public interface IChannelSettingsRepository
{
    Task<ChannelSettings?> GetByChannelIdAsync(string channelId, CancellationToken cancellationToken);

    Task UpsertAsync(ChannelSettings settings, CancellationToken cancellationToken);
}
