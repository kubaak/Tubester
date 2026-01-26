using Microsoft.EntityFrameworkCore;
using YouTubester.Persistence.Credits;

namespace YouTubester.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(YouTubesterDb databaseContext, CancellationToken cancellationToken = default)
    {
        if (!await databaseContext.ActionCosts.AnyAsync(cancellationToken))
        {
            var currentDateTimeUtc = DateTimeOffset.UtcNow;

            var initialActionCosts = new List<ActionCost>
            {
                new()
                {
                    ActionType = "CopyTemplateExecuted",
                    Cost = 1,
                    IsEnabled = true,
                    UpdatedAtUtc = currentDateTimeUtc,
                    Notes = "Cost to copy template metadata between videos."
                },
                new()
                {
                    ActionType = "AiTemplateEnqueued",
                    Cost = 1,
                    IsEnabled = true,
                    UpdatedAtUtc = currentDateTimeUtc,
                    Notes = "Cost to enqueue an AI template job."
                },
                new()
                {
                    ActionType = "AiTemplateSubmitted",
                    Cost = 1,
                    IsEnabled = true,
                    UpdatedAtUtc = currentDateTimeUtc,
                    Notes = "Cost to submit AI-generated template changes (initially free)."
                },
                new()
                {
                    ActionType = "AiReplyGenerated",
                    Cost = 0,
                    IsEnabled = true,
                    UpdatedAtUtc = currentDateTimeUtc,
                    Notes = "Cost to generate an AI reply for a comment."
                },
                new()
                {
                    ActionType = "ReplyPostedToYouTube",
                    Cost = 1,
                    IsEnabled = true,
                    UpdatedAtUtc = currentDateTimeUtc,
                    Notes = "Posting replies to YouTube is initially free."
                }
            };

            await databaseContext.ActionCosts.AddRangeAsync(initialActionCosts, cancellationToken);
            await databaseContext.SaveChangesAsync(cancellationToken);
        }
    }
}