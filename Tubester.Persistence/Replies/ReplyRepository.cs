using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Replies;
using Tubester.Domain;

namespace Tubester.Persistence.Replies;

public class ReplyRepository(TubesterDb db) : IReplyRepository
{
    public async Task<Reply?> GetReplyAsync(string commentId, CancellationToken cancellationToken)
    {
        return await db.Replies.AsNoTracking().FirstOrDefaultAsync(d => d.CommentId == commentId, cancellationToken);
    }

    public async Task<bool> TryClaimForDraftingAsync(Reply reply, CancellationToken cancellationToken)
    {
        var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""

             INSERT INTO "Replies" ("CommentId", "VideoId", "VideoTitle", "CommentText", "Status", "PulledAt", "OriginalCommentAt")
             VALUES ({reply.CommentId}, {reply.VideoId}, {reply.VideoTitle}, {reply.CommentText}, {(int)ReplyStatus.Drafting}, 
             {reply.PulledAt}, {reply.OriginalCommentAt})
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
            tracked.TransferEventsFrom(reply);
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

    public async Task<List<Reply>> GetRepliesPageAsync(
        string channelId,
        IReadOnlyCollection<ReplyStatus>? statuses,
        IReadOnlyCollection<string>? videoIds,
        string? originalComment,
        DateTimeOffset? afterOriginalCommentAtUtc,
        string? afterCommentId,
        int take,
        CancellationToken cancellationToken)
    {
        if (take <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(take), "Take must be greater than 0.");
        }

        var hasAfterOriginalCommentAtUtc = afterOriginalCommentAtUtc.HasValue;
        var hasAfterCommentId = !string.IsNullOrWhiteSpace(afterCommentId);

        if (hasAfterOriginalCommentAtUtc != hasAfterCommentId)
        {
            throw new ArgumentException(
                "Cursor must contain both afterOriginalCommentAtUtc and afterCommentId, or neither.");
        }

        var query = db.Replies
            .AsNoTracking()
            .Join(
                db.Videos,
                r => r.VideoId,
                v => v.VideoId,
                (r, v) => new { Reply = r, Video = v })
            .Join(
                db.Channels.Where(c => c.ChannelId == channelId),
                rv => rv.Video.UploadsPlaylistId,
                c => c.UploadsPlaylistId,
                (rv, _) => rv.Reply);

        if (statuses is { Count: > 0 })
        {
            query = query.Where(r => statuses.Contains(r.Status));
        }

        if (videoIds is { Count: > 0 })
        {
            query = query.Where(r => videoIds.Contains(r.VideoId));
        }

        if (!string.IsNullOrWhiteSpace(originalComment))
        {
            var pattern = $"%{EscapeLikePattern(originalComment.Trim())}%";
            query = query.Where(r => EF.Functions.ILike(r.CommentText, pattern, @"\"));
        }

        if (hasAfterOriginalCommentAtUtc)
        {
            var cursorOriginalCommentAtUtc = afterOriginalCommentAtUtc!.Value;
            var cursorCommentId = afterCommentId!;

            query = query.Where(r =>
                r.OriginalCommentAt < cursorOriginalCommentAtUtc ||
                (r.OriginalCommentAt == cursorOriginalCommentAtUtc && r.CommentId.CompareTo(cursorCommentId) < 0));
        }

        return await query
            .OrderByDescending(r => r.OriginalCommentAt)
            .ThenByDescending(r => r.CommentId)
            .Take(take)
            .ToListAsync(cancellationToken);

        static string EscapeLikePattern(string value) =>
            value
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");
    }
}
