namespace Tubester.Application.Channels;

public sealed record ChannelSettingsDto(
    string ChannelId,
    bool IsCommentAssistantEnabled,
    bool IsSuggestRepliesForTopLevelCommentsOnly,
    int MaxSuggestedRepliesPerSync,
    int MaxCommentAgeDays,
    string ReplyLanguage,
    string? ResponseForNonTextualComments,
    DateTimeOffset UpdatedAtUtc);
