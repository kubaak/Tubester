using Microsoft.EntityFrameworkCore;
using YouTubester.Abstractions.Replies;
using YouTubester.Domain;

namespace YouTubester.Persistence.Replies;

public class ReplyRepository(YouTubesterDb db) : IReplyRepository
{
    public async Task<IEnumerable<Reply>> GetRepliesForApprovalAsync(string channelId,
        CancellationToken cancellationToken)
    {
        return await db.Replies
            .AsNoTracking()
            .Where(r => r.Status == ReplyStatus.Suggested)
            .Join(
                db.Videos,
                r => r.VideoId,
                v => v.VideoId,
                (r, v) => new { r, v }
            )
            .Join(
                db.Channels.Where(c => c.ChannelId == channelId),
                rv => rv.v.UploadsPlaylistId,
                c => c.UploadsPlaylistId,
                (rv, c) => rv.r
            )
            .OrderByDescending(r => r.PostedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Reply?> GetReplyAsync(string commentId, CancellationToken cancellationToken)
    {
        return await db.Replies.AsNoTracking().FirstOrDefaultAsync(d => d.CommentId == commentId, cancellationToken);
    }

    public async Task<bool> TryClaimForDraftingAsync(Reply reply, CancellationToken cancellationToken)
    {
        var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""

             INSERT INTO "Replies" ("CommentId", "VideoId", "VideoTitle", "CommentText", "Status", "PulledAt")
             VALUES ({reply.CommentId}, {reply.VideoId}, {reply.VideoTitle}, {reply.CommentText}, {(int)ReplyStatus.Drafting}, {reply.PulledAt})
             ON CONFLICT ("CommentId") DO NOTHING

             """, cancellationToken);

        return rowsAffected > 0;
    }

    public async Task AddOrUpdateReplyAsync(Reply reply, CancellationToken ct)
    {
        var tracked = await db.Replies.FirstOrDefaultAsync(r => r.CommentId == reply.CommentId, ct);

        if (tracked is null)
        {
            db.Replies.Add(reply);
        }
        else
        {
            db.Entry(tracked).CurrentValues.SetValues(reply);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<Reply?> DeleteReplyAsync(string commentId, CancellationToken cancellationToken)
    {
        var entity = await db.Replies
            .FirstOrDefaultAsync(r => r.CommentId == commentId, cancellationToken);

        if (entity is not null)
        {
            db.Replies.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
        }

        return entity;
    }

    public async Task<List<(string CommentId, ReplyStatus Status)>> LoadStatusesAsync(IEnumerable<string> ids,
        CancellationToken ct)
    {
        return await db.Replies.AsNoTracking()
            .Where(r => ids.Contains(r.CommentId))
            .Select(r => new ValueTuple<string, ReplyStatus>(r.CommentId, r.Status))
            .ToListAsync(ct);
    }

    public async Task<string[]> IgnoreManyAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.ToArray();
        if (list.Length == 0)
        {
            return [];
        }

        _ = await db.Replies
            .Where(r => list.Contains(r.CommentId))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, _ => ReplyStatus.Ignored), ct);
        return list;
    }
}