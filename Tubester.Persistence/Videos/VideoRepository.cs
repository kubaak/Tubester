using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Videos;
using Tubester.Domain;

namespace Tubester.Persistence.Videos;

public sealed class VideoRepository(TubesterDb db) : IVideoRepository
{
    public async Task<List<Video>> GetCommentableVideosAsync(string channelId, CancellationToken cancellationToken)
    {
        return await db.Videos
            .AsNoTracking()
            .Where(video => video.CommentsAllowed ?? true)
            .Join(
                db.Channels.Where(channel => channel.ChannelId == channelId),
                video => video.UploadsPlaylistId,
                channel => channel.UploadsPlaylistId,
                (video, channel) => video
            )
            .OrderByDescending(video => video.PublishedAt)
            .ThenByDescending(video => video.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Video?> GetVideoByIdAsync(string channelId, string videoId, CancellationToken cancellationToken)
    {
        return await db.Videos
            .AsNoTracking()
            .Join(
                db.Channels.Where(channel => channel.ChannelId == channelId),
                video => video.UploadsPlaylistId,
                channel => channel.UploadsPlaylistId,
                (video, channel) => video
            )
            .FirstOrDefaultAsync(video => video.VideoId == videoId, cancellationToken);
    }

    public async Task<(int inserted, int updated)> UpsertAsync(
        string channelId,
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
            .Join(
                db.Channels.Where(channel => channel.ChannelId == channelId),
                video => video.UploadsPlaylistId,
                channel => channel.UploadsPlaylistId,
                (video, channel) => video
            )
            .Where(video => videoIds.Contains(video.VideoId))
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

    public async Task<Dictionary<string, string?>> GetVideoETagsAsync(
        string channelId,
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
            .Join(
                db.Channels.Where(channel => channel.ChannelId == channelId),
                video => video.UploadsPlaylistId,
                channel => channel.UploadsPlaylistId,
                (video, channel) => video
            )
            .Where(video => videoIdsList.Contains(video.VideoId))
            .ToDictionaryAsync(video => video.VideoId, video => video.ETag, cancellationToken);
    }

    public async Task<bool> VideoExistsForChannelAsync(
        string channelId,
        string videoId,
        CancellationToken cancellationToken)
    {
        return await db.Videos
            .AsNoTracking()
            .Join(
                db.Channels.Where(channel => channel.ChannelId == channelId),
                video => video.UploadsPlaylistId,
                channel => channel.UploadsPlaylistId,
                (video, channel) => video
            )
            .AnyAsync(video => video.VideoId == videoId, cancellationToken);
    }

    public async Task<bool> TrySettingAiTemplateInProgressAsync(
        string channelId,
        string videoId,
        bool isAiTemplateInProgress,
        CancellationToken cancellationToken)
    {
        var video = await db.Videos
            .Join(
                db.Channels.Where(channel => channel.ChannelId == channelId),
                currentVideo => currentVideo.UploadsPlaylistId,
                channel => channel.UploadsPlaylistId,
                (currentVideo, channel) => currentVideo
            )
            .FirstOrDefaultAsync(currentVideo => currentVideo.VideoId == videoId, cancellationToken);

        if (video is null)
        {
            return false;
        }

        video.SetAiTemplateInProgress(isAiTemplateInProgress);
        await db.SaveChangesAsync(cancellationToken);
        return video.IsAiTemplateInProgress;
    }

    public Task MarkCommentsDisabledAsync(string channelId, string videoId, CancellationToken cancellationToken)
    {
        return db.Videos
            .Where(v => v.VideoId == videoId)
            .Join(
                db.Channels.Where(channel => channel.ChannelId == channelId),
                currentVideo => currentVideo.UploadsPlaylistId,
                channel => channel.UploadsPlaylistId,
                (currentVideo, channel) => currentVideo
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(v => v.CommentsAllowed, false),
                cancellationToken);
    }
}