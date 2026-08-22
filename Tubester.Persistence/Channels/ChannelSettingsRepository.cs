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
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ChannelSettings" (
                "ChannelId",
                "IsCommentAssistantEnabled",
                "IsSuggestRepliesForTopLevelCommentsOnly",
                "MaxSuggestedRepliesPerSync",
                "MaxCommentAgeDays",
                "ReplyLanguage",
                "ResponseForNonTextualComments",
                "UpdatedAtUtc")
            VALUES (
                {settings.ChannelId},
                {settings.IsCommentAssistantEnabled},
                {settings.IsSuggestRepliesForTopLevelCommentsOnly},
                {settings.MaxSuggestedRepliesPerSync},
                {settings.MaxCommentAgeDays},
                {settings.ReplyLanguage},
                {settings.ResponseForNonTextualComments},
                {settings.UpdatedAtUtc})
            ON CONFLICT ("ChannelId") DO UPDATE
            SET
                "IsCommentAssistantEnabled" = EXCLUDED."IsCommentAssistantEnabled",
                "IsSuggestRepliesForTopLevelCommentsOnly" = EXCLUDED."IsSuggestRepliesForTopLevelCommentsOnly",
                "MaxSuggestedRepliesPerSync" = EXCLUDED."MaxSuggestedRepliesPerSync",
                "MaxCommentAgeDays" = EXCLUDED."MaxCommentAgeDays",
                "ReplyLanguage" = EXCLUDED."ReplyLanguage",
                "ResponseForNonTextualComments" = EXCLUDED."ResponseForNonTextualComments",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """, cancellationToken);

        var entry = db.Entry(settings);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Unchanged;
        }
    }
}
