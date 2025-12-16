using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using YouTubester.Abstractions.Videos;
using YouTubester.Domain;

namespace YouTubester.Persistence.Videos;

public sealed class VideoRepository(YouTubesterDb db) : IVideoRepository
{
    public async Task<List<Video>> GetCommentableVideosAsync(string channelId, CancellationToken cancellationToken)
    {
        return await db.Videos
            .AsNoTracking()
            .Where(v => v.CommentsAllowed ?? true)
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
            .OrderByDescending(v => v.PublishedAt)
            .ThenByDescending(v => v.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Video?> GetVideoByIdAsync(string videoId, CancellationToken cancellationToken)
    {
        return await db.Videos
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.VideoId == videoId, cancellationToken);
    }

    public async Task<(int inserted, int updated)> UpsertAsync(IEnumerable<Video> videos,
        CancellationToken cancellationToken)
    {
        var list = videos.ToList();
        if (list.Count == 0)
        {
            return (0, 0);
        }

        var ids = list.Select(i => i.VideoId).ToHashSet();

        var existing = await db.Videos.Where(v => ids.Contains(v.VideoId))
            .ToDictionaryAsync(v => v.VideoId, v => v, cancellationToken);

        var now = DateTimeOffset.UtcNow; //todo provider
        var inserts = 0;
        var updates = 0;

        foreach (var video in list)
        {
            if (!existing.TryGetValue(video.VideoId, out var row))
            {
                db.Add(video);
                inserts++;
            }
            else
            {
                var changed = row.ApplyDetails(
                    video.Title, video.Description, video.PublishedAt, video.Duration,
                    video.Visibility, video.Tags, video.CategoryId, video.DefaultLanguage,
                    video.DefaultAudioLanguage, video.Location, video.LocationDescription, now, video.ETag,
                    video.CommentsAllowed
                );
                if (changed)
                {
                    updates++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (inserts, updates);
    }

    public async Task<List<Video>> GetVideosPageAsync(
        string channelId,
        string? title,
        IReadOnlyCollection<VideoVisibility>? visibilities,
        DateTimeOffset? afterPublishedAtUtc,
        string? afterVideoId,
        int take,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SELECT *");
        sb.AppendLine("FROM Videos v");
        sb.AppendLine("INNER JOIN Channels c ON c.UploadsPlaylistId = v.UploadsPlaylistId");
        sb.AppendLine("WHERE 1=1");
        sb.AppendLine("  AND c.ChannelId = @channelId");

        var parameters = new List<object> { new SqliteParameter("@channelId", channelId) };

        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.AppendLine("  AND v.Title IS NOT NULL AND v.Title COLLATE NOCASE LIKE '%' || @title || '%'");
            parameters.Add(new SqliteParameter("@title", title));
        }

        if (visibilities is { Count: > 0 })
        {
            var inParams = new List<string>();
            var i = 0;
            foreach (var v in visibilities)
            {
                var name = $"@vis{i++}";
                inParams.Add(name);
                parameters.Add(new SqliteParameter(name, (int)v));
            }

            sb.AppendLine($"  AND v.Visibility IN ({string.Join(", ", inParams)})");
        }

        if (afterPublishedAtUtc.HasValue && !string.IsNullOrEmpty(afterVideoId))
        {
            sb.AppendLine("  AND (v.PublishedAt < @afterPub");
            sb.AppendLine("       OR (v.PublishedAt = @afterPub AND v.VideoId COLLATE BINARY < @afterId))");

            // With Microsoft.Data.Sqlite it’s safest to pass the DateTime value EF maps to
            parameters.Add(new SqliteParameter("@afterPub", afterPublishedAtUtc.Value.UtcDateTime));
            parameters.Add(new SqliteParameter("@afterId", afterVideoId));
        }

        sb.AppendLine("ORDER BY v.PublishedAt DESC, v.VideoId DESC");
        sb.AppendLine("LIMIT @take");

        parameters.Add(new SqliteParameter("@take", take));

        var sql = sb.ToString();

        return await db.Videos
            .FromSqlRaw(sql, parameters.ToArray())
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<Dictionary<string, string?>> GetVideoETagsAsync(IEnumerable<string> videoIds,
        CancellationToken cancellationToken)
    {
        var videoIdsList = videoIds.ToList();
        if (videoIdsList.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        return await db.Videos
            .AsNoTracking()
            .Where(v => videoIdsList.Contains(v.VideoId))
            .ToDictionaryAsync(v => v.VideoId, v => v.ETag, cancellationToken);
    }
}