using Microsoft.EntityFrameworkCore;
using YouTubester.Domain;

namespace YouTubester.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(YouTubesterDb db, CancellationToken cancellationToken = default)
    {
        if (!await db.Replies.AnyAsync(cancellationToken))
        {
            db.Replies.AddRange(
                Reply.Create(
                    "demo-1", "demo-video-1", "Compilation", "This is amazing!",
                    DateTimeOffset.UtcNow)
            );

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}