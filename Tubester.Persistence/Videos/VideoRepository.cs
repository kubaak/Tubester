using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions;
using Tubester.Abstractions.Videos;
using Tubester.Domain;

namespace Tubester.Persistence.Videos;

public sealed class VideoRepository(TubesterDb db, IDateTimeOffsetProvider dateTimeProvider) : IVideoRepository
{
    public async Task<List<Video>> GetCommentableVideosAsync(string uploadPlaylistId, CancellationToken cancellationToken)
    {
        return await db.Videos
            .AsNoTracking()
            .Where(video => video.UploadsPlaylistId == uploadPlaylistId && (video.CommentsAllowed ?? true) &&
                            video.Visibility == VideoVisibility.Public)
            .OrderByDescending(video => video.PublishedAt)
            .ThenByDescending(video => video.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Video?> GetVideoByIdAsync(string uploadPlaylistId, string videoId, CancellationToken cancellationToken)
    {
        return await db.Videos
            .AsNoTracking()
            .FirstOrDefaultAsync(video => video.UploadsPlaylistId == uploadPlaylistId && video.VideoId == videoId, cancellationToken);
    }

    public async Task<(int inserted, int updated)> UpsertAsync(
        string uploadPlaylistId,
        IEnumerable<Video> videos,
        CancellationToken cancellationToken)
    {
        var videoList = videos.ToList();
        if (videoList.Count == 0)
        {
            return (0, 0);
        }

        var videoIds = videoList.Select(video => video.VideoId).ToHashSet();

        var existingVideosById = await db.Videos
            .Where(video => video.UploadsPlaylistId == uploadPlaylistId && videoIds.Contains(video.VideoId))
            .ToDictionaryAsync(video => video.VideoId, video => video, cancellationToken);

        var currentTimeUtc = dateTimeProvider.GetUtcNowDateTimeOffset();
        var inserted = 0;
        var updated = 0;

        foreach (var video in videoList)
        {
            if (!existingVideosById.TryGetValue(video.VideoId, out var existingVideo))
            {
                db.Add(video);
                inserted++;
            }
            else
            {
                var changed = existingVideo.ApplyDetails(
                    video.Title, video.Description, video.PublishedAt, video.Duration,
                    video.Visibility, video.Tags, video.CategoryId, video.DefaultLanguage,
                    video.DefaultAudioLanguage, currentTimeUtc, video.ETag,
                    video.CommentsAllowed
                );
                existingVideo.TransferEventsFrom(video);
                if (changed)
                {
                    updated++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (inserted, updated);
    }

    public async Task<List<Video>> GetVideosPageAsync(
        string uploadPlaylistId,
        string? title,
        IReadOnlyCollection<VideoVisibility>? visibilities,
        DateTimeOffset? afterPublishedAtUtc,
        string? afterVideoId,
        int take,
        CancellationToken cancellationToken)
    {
        var videosQuery = db.Videos
            .AsNoTracking()
            .Where(video => video.UploadsPlaylistId == uploadPlaylistId);

        if (!string.IsNullOrWhiteSpace(title))
        {
            videosQuery = videosQuery.Where(video => video.Title != null &&
                                                     EF.Functions.ILike(video.Title, $"%{title}%"));
        }

        if (visibilities is { Count: > 0 })
        {
            videosQuery = videosQuery.Where(video => visibilities.Contains(video.Visibility));
        }

        if (afterPublishedAtUtc.HasValue && !string.IsNullOrWhiteSpace(afterVideoId))
        {
            videosQuery = videosQuery.Where(video =>
                video.PublishedAt < afterPublishedAtUtc.Value ||
                (video.PublishedAt == afterPublishedAtUtc.Value && video.VideoId.CompareTo(afterVideoId) < 0));
        }

        return await videosQuery
            .OrderByDescending(video => video.PublishedAt)
            .ThenByDescending(video => video.VideoId)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, string?>> GetVideoETagsAsync(
        string uploadPlaylistId,
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken)
    {
        var videoIdsList = videoIds.ToList();
        if (videoIdsList.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        return await db.Videos
            .AsNoTracking()
            .Where(video => video.UploadsPlaylistId == uploadPlaylistId && videoIdsList.Contains(video.VideoId))
            .ToDictionaryAsync(video => video.VideoId, video => video.ETag, cancellationToken);
    }

    public async Task<bool> VideoExistsForChannelAsync(
        string uploadPlaylistId,
        string videoId,
        CancellationToken cancellationToken)
    {
        return await db.Videos
            .AsNoTracking()
            .AnyAsync(video => video.UploadsPlaylistId == uploadPlaylistId && video.VideoId == videoId, cancellationToken);
    }

    public async Task<bool> TryAddAiOperationsInProgressAsync(
        string uploadPlaylistId,
        string videoId,
        AiVideoOperationFlags operations,
        CancellationToken cancellationToken)
    {
        if (operations == AiVideoOperationFlags.None)
        {
            return true;
        }

        var operationsValue = (int)operations;

        var affectedRows = await db.Database.ExecuteSqlInterpolatedAsync($"""
             UPDATE "Videos"
             SET "AiOperationsInProgress" = "AiOperationsInProgress" | {operationsValue}
             WHERE "UploadsPlaylistId" = {uploadPlaylistId}
               AND "VideoId" = {videoId}
               AND ("AiOperationsInProgress" & {operationsValue}) = 0;
             """, cancellationToken);

        return affectedRows == 1;
    }

    public async Task<bool> TryClearAiOperationsInProgressAsync(
        string uploadPlaylistId,
        string videoId,
        AiVideoOperationFlags operations,
        CancellationToken cancellationToken)
    {
        if (operations == AiVideoOperationFlags.None)
        {
            return true;
        }

        var operationsValue = (int)operations;

        var affectedRows = await db.Database.ExecuteSqlInterpolatedAsync($"""
             UPDATE "Videos"
             SET "AiOperationsInProgress" = "AiOperationsInProgress" & ~{operationsValue}
             WHERE "UploadsPlaylistId" = {uploadPlaylistId}
               AND "VideoId" = {videoId};
             """, cancellationToken);

        return affectedRows == 1;
    }

    public Task MarkCommentsDisabledAsync(string uploadPlaylistId, string videoId, CancellationToken cancellationToken)
    {
        return db.Videos
            .Where(v => v.VideoId == videoId && v.UploadsPlaylistId == uploadPlaylistId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(v => v.CommentsAllowed, false),
                cancellationToken);
    }
}