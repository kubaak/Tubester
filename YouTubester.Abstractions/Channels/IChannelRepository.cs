using YouTubester.Domain;

namespace YouTubester.Abstractions.Channels;

public interface IChannelRepository
{
    Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken);

    Task SetUploadsCutoffAsync(string channelId, DateTimeOffset cutoff, CancellationToken cancellationToken);

    Task UpsertChannelAsync(Channel channel, CancellationToken cancellationToken);
}