namespace Tubester.Domain;

public sealed class ChannelSettings : Entity
{
    public string ChannelId { get; private set; } = null!;
    public bool IsCommentAssistantEnabled { get; private set; }
    public bool IsSuggestRepliesForTopLevelCommentsOnly { get; private set; }
    public int MaxSuggestedRepliesPerSync { get; private set; }
    public int MaxCommentAgeDays { get; private set; }
    public string ReplyLanguage { get; private set; } = null!;
    public string? ResponseForNonTextualComments { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ChannelSettings CreateDefault(string channelId, DateTimeOffset nowUtc)
    {
        RequireUtc(nowUtc);

        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("Channel id is required.", nameof(channelId));
        }

        return new ChannelSettings
        {
            ChannelId = channelId,
            IsCommentAssistantEnabled = false,
            IsSuggestRepliesForTopLevelCommentsOnly = true,
            MaxSuggestedRepliesPerSync = 10,
            MaxCommentAgeDays = 10,
            ReplyLanguage = "English",
            ResponseForNonTextualComments = null,
            UpdatedAtUtc = nowUtc
        };
    }

    public void Apply(
        bool isCommentAssistantEnabled,
        bool isSuggestRepliesForTopLevelCommentsOnly,
        int maxSuggestedRepliesPerSync,
        int maxCommentAgeDays,
        string replyLanguage,
        string? responseForNonTextualComments,
        DateTimeOffset nowUtc)
    {
        RequireUtc(nowUtc);

        if (maxSuggestedRepliesPerSync < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSuggestedRepliesPerSync), "Must be >= 0.");
        }

        if (maxCommentAgeDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCommentAgeDays), "Must be >= 0.");
        }

        if (string.IsNullOrWhiteSpace(replyLanguage))
        {
            throw new ArgumentException("Reply language is required.", nameof(replyLanguage));
        }

        IsCommentAssistantEnabled = isCommentAssistantEnabled;
        IsSuggestRepliesForTopLevelCommentsOnly = isSuggestRepliesForTopLevelCommentsOnly;
        MaxSuggestedRepliesPerSync = maxSuggestedRepliesPerSync;
        MaxCommentAgeDays = maxCommentAgeDays;
        ReplyLanguage = replyLanguage.Trim();
        ResponseForNonTextualComments = string.IsNullOrWhiteSpace(responseForNonTextualComments)
            ? null
            : responseForNonTextualComments.Trim();
        UpdatedAtUtc = nowUtc;
    }

    private ChannelSettings()
    {
    }
}
