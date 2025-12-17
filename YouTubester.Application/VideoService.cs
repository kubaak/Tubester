using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubester.Abstractions.Channels;
using YouTubester.Abstractions.Videos;
using YouTubester.Application.Common;
using YouTubester.Application.Contracts;
using YouTubester.Application.Contracts.Videos;
using YouTubester.Application.Exceptions;
using YouTubester.Application.Options;
using YouTubester.Domain;
using YouTubester.Integration;

namespace YouTubester.Application;

public class VideoService(
    IVideoRepository repo,
    ICurrentChannelContext channelContext,
    IOptions<VideoListingOptions> options,
    ILogger<VideoService> logger) : IVideoService
{
    public async Task<PagedResult<VideoListItemDto>> GetVideosAsync(string? title, VideoVisibility[]? visibility,
        int? pageSize, string? pageToken, CancellationToken ct)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (string.IsNullOrEmpty(normalizedTitle))
        {
            normalizedTitle = null;
        }

        var opts = options.Value;
        var effectivePageSize = pageSize ?? opts.DefaultPageSize;
        if (effectivePageSize < 1 || effectivePageSize > opts.MaxPageSize)
        {
            throw new InvalidPageSizeException(effectivePageSize, opts.MaxPageSize);
        }

        var visibilityBinding = visibility is null ? string.Empty : string.Join(',', visibility.OrderBy(v => v));
        var binding = $"{normalizedTitle ?? string.Empty}|{visibilityBinding}";

        DateTimeOffset? afterPublishedAtUtc = null;
        string? afterVideoId = null;
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            if (!VideosPageToken.TryParse(pageToken, out var publishedAt, out var videoId, out var tokenBinding))
            {
                logger.LogWarning("Invalid page token received");
                throw new InvalidPageTokenException();
            }

            if (!string.IsNullOrEmpty(tokenBinding) && !string.Equals(tokenBinding, binding, StringComparison.Ordinal))
            {
                logger.LogWarning("Page token binding mismatch");
                throw new InvalidPageTokenException("Page token does not match current filters.");
            }

            afterPublishedAtUtc = publishedAt;
            afterVideoId = videoId;
        }

        var channelId = channelContext.GetRequiredChannelId();

        // Fetch one extra item to determine if there's a next page
        var take = effectivePageSize + 1;
        var videos =
            await repo.GetVideosPageAsync(channelId, normalizedTitle, visibility, afterPublishedAtUtc, afterVideoId,
                take, ct);

        // Determine if there are more items
        var hasMore = videos.Count > effectivePageSize;
        var itemsToReturn = hasMore ? videos.Take(effectivePageSize).ToList() : videos;

        string? nextPageToken = null;
        if (hasMore && itemsToReturn.Count > 0)
        {
            var lastItem = itemsToReturn[^1];
            nextPageToken = VideosPageToken.Serialize(lastItem.PublishedAt, lastItem.VideoId, binding);
        }

        var items = itemsToReturn.Select(v => new VideoListItemDto
        {
            VideoId = v.VideoId, Title = v.Title, PublishedAt = v.PublishedAt, ThumbnailUrl = v.ThumbnailUrl
        }).ToList();

        return new PagedResult<VideoListItemDto> { Items = items, NextPageToken = nextPageToken };
    }
}