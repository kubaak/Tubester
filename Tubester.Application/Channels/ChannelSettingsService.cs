using Tubester.Abstractions.Channels;
using Tubester.Domain;

namespace Tubester.Application.Channels;

public sealed class ChannelSettingsService(
    IChannelSettingsRepository channelSettingsRepository,
    IDateTimeOffsetProvider dateTimeOffsetProvider) : IChannelSettingsService
{
    public async Task<ChannelSettingsDto> GetOrCreateAsync(string channelId, CancellationToken cancellationToken)
    {
        var settings = await channelSettingsRepository.GetByChannelIdAsync(channelId, cancellationToken);

        if (settings is not null)
        {
            return ToDto(settings);
        }

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
        settings = ChannelSettings.CreateDefault(channelId, nowUtc);
        await channelSettingsRepository.UpsertAsync(settings, cancellationToken);

        return ToDto(settings);
    }

    public async Task<ChannelSettingsDto> UpdateAsync(string channelId, UpdateChannelSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await channelSettingsRepository.GetByChannelIdAsync(channelId, cancellationToken);

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        if (settings is null)
        {
            settings = ChannelSettings.CreateDefault(channelId, nowUtc);
            await channelSettingsRepository.UpsertAsync(settings, cancellationToken);
        }

        settings.Apply(
            request.IsCommentAssistantEnabled,
            request.IsSuggestRepliesForTopLevelCommentsOnly,
            request.MaxSuggestedRepliesPerSync,
            request.MaxCommentAgeDays,
            request.ReplyLanguage,
            request.ResponseForNonTextualComments,
            nowUtc);

        await channelSettingsRepository.UpsertAsync(settings, cancellationToken);

        return ToDto(settings);
    }

    private static ChannelSettingsDto ToDto(ChannelSettings settings)
    {
        return new ChannelSettingsDto(
            settings.ChannelId,
            settings.IsCommentAssistantEnabled,
            settings.IsSuggestRepliesForTopLevelCommentsOnly,
            settings.MaxSuggestedRepliesPerSync,
            settings.MaxCommentAgeDays,
            settings.ReplyLanguage,
            settings.ResponseForNonTextualComments,
            settings.UpdatedAtUtc);
    }
}
