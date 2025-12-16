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
            .FirstOrDefaultAsync(video => video.VideoId == videoId, cancellationToken);
    }

    public async Task<(int inserted, int updated)> UpsertAsync(IEnumerable<Video> videos,
        CancellationToken cancellationToken)
    {
        var videoList = videos.ToList();
        if (videoList.Count == 0)
        {
            return (0, 0);
        }

        var videoIds = videoList.Select(video => video.VideoId).ToHashSet();

        var existingVideosById = await db.Videos.Where(video => videoIds.Contains(video.VideoId))
            .ToDictionaryAsync(video => video.VideoId, video => video, cancellationToken);

        var currentTimeUtc = DateTimeOffset.UtcNow; //todo provider
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
                    video.DefaultAudioLanguage, video.Location, video.LocationDescription, currentTimeUtc, video.ETag,
                    video.CommentsAllowed
                );
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
        string channelId,
        string? title,
        IReadOnlyCollection<VideoVisibility>? visibilities,
        DateTimeOffset? afterPublishedAtUtc,
        string? afterVideoId,
        int take,
        CancellationToken cancellationToken)
    {
        var videosQuery = db.Videos
            .AsNoTracking()
            .Join(
                db.Channels.Where(channel => channel.ChannelId == channelId),
                video => video.UploadsPlaylistId,
                channel => channel.UploadsPlaylistId,
                (video, channel) => video
            );

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
            .Where(video => videoIdsList.Contains(video.VideoId))
            .ToDictionaryAsync(video => video.VideoId, video => video.ETag, cancellationToken);
    }
}