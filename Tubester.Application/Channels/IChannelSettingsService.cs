namespace Tubester.Application.Channels;

public interface IChannelSettingsService
{
    Task<ChannelSettingsDto> GetOrCreateAsync(string channelId, CancellationToken cancellationToken);

    Task<ChannelSettingsDto> UpdateAsync(string channelId, UpdateChannelSettingsRequest request,
        CancellationToken cancellationToken);
}
