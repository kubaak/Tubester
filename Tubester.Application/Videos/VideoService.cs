using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Playlists;
using Tubester.Abstractions.Videos;
using Tubester.Application.Common;
using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Videos;
using Tubester.Application.Credits;
using Tubester.Application.Exceptions;
using Tubester.Application.Options;
using Tubester.Domain;
using Tubester.Integration;

namespace Tubester.Application.Videos;

public class VideoService(
    IVideoRepository videoRepository,
    IPlaylistRepository playlistRepository,
    ICurrentChannelContext channelContext,
    IOptions<VideoListingOptions> videoListingOptions,
    IYouTubeIntegration youTubeIntegration,
    ILogger<VideoService> videoLogger,
    IUserEventLogger userEventLogger,
    ICreditsService creditsService,
    IDateTimeOffsetProvider dateTimeOffsetProvider) : IVideoService
{
    public async Task<PagedResult<VideoListItemDto>> GetVideosAsync(string? title, VideoVisibility[]? visibility,
        int? pageSize, string? pageToken, CancellationToken ct)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (string.IsNullOrEmpty(normalizedTitle))
        {
            normalizedTitle = null;
        }

        var options = videoListingOptions.Value;
        var effectivePageSize = pageSize ?? options.DefaultPageSize;
        if (effectivePageSize < 1 || effectivePageSize > options.MaxPageSize)
        {
            throw new InvalidPageSizeException(effectivePageSize, options.MaxPageSize);
        }

        var visibilityBinding = visibility is null
            ? string.Empty
            : string.Join(',', visibility.OrderBy(currentVisibility => currentVisibility));
        var binding = $"{normalizedTitle ?? string.Empty}|{visibilityBinding}";

        DateTimeOffset? afterPublishedAtUtc = null;
        string? afterVideoId = null;
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            if (!VideosPageToken.TryParse(pageToken, out var publishedAt, out var videoId, out var tokenBinding))
            {
                videoLogger.LogWarning("Invalid page token received");
                throw new InvalidPageTokenException();
            }

            if (!string.IsNullOrEmpty(tokenBinding) && !string.Equals(tokenBinding, binding, StringComparison.Ordinal))
            {
                videoLogger.LogWarning("Page token binding mismatch");
                throw new InvalidPageTokenException("Page token does not match current filters.");
            }

            afterPublishedAtUtc = publishedAt;
            afterVideoId = videoId;
        }

        var channelId = channelContext.GetRequiredChannelId();

        // Fetch one extra item to determine if there's a next page
        var take = effectivePageSize + 1;
        var videos =
            await videoRepository.GetVideosPageAsync(channelId, normalizedTitle, visibility, afterPublishedAtUtc,
                afterVideoId,
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

        var items = itemsToReturn.Select(video => new VideoListItemDto
        {
            VideoId = video.VideoId,
            Title = video.Title,
            PublishedAt = video.PublishedAt,
            ThumbnailUrl = video.ThumbnailUrl
        }).ToList();

        return new PagedResult<VideoListItemDto> { Items = items, NextPageToken = nextPageToken };
    }

    public async Task<VideoDetailsDto?> GetVideoDetailsAsync(string videoId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        var channelId = channelContext.GetRequiredChannelId();
        var video = await videoRepository.GetVideoByIdAsync(channelId, videoId, cancellationToken);

        if (video is null)
        {
            return null;
        }

        return new VideoDetailsDto
        {
            Title = video.Title,
            Description = video.Description,
            Tags = video.Tags,
            IsAiTemplateInProgress = video.IsAiTemplateInProgress
        };
    }

    public async Task<CopyVideoTemplateResult> CopyTemplateAsync(
        string userId,
        CopyVideoTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var channelId = channelContext.GetRequiredChannelId();

        var copyTemplateIdempotencyKey =
            $"copy-template:{userId}:{request.SourceVideoId}:{request.TargetVideoId}:{request.OperationId}";


        // First, check and deduct credits. This acts as a gate before performing external work.
        var copyTemplateSpendSucceeded = await creditsService.TrySpendAsync(
            userId,
            CreditActionType.CopyTemplateExecuted.ToString(),
            copyTemplateIdempotencyKey,
            request.TargetVideoId,
            new { sourceVideoId = request.SourceVideoId, targetVideoId = request.TargetVideoId },
            cancellationToken);

        if (!copyTemplateSpendSucceeded)
        {
            throw new ForbiddenException("Insufficient credits to copy template.");
        }

        try
        {
            // Load source and target videos from DB
            var sourceVideo =
                await videoRepository.GetVideoByIdAsync(channelId, request.SourceVideoId, cancellationToken)
                ?? throw new ArgumentException($"Source video {request.SourceVideoId} not found in cache.");

            var targetVideo =
                await videoRepository.GetVideoByIdAsync(channelId, request.TargetVideoId, cancellationToken)
                ?? throw new ArgumentException($"Target video {request.TargetVideoId} not found in cache.");

            // Build effective metadata starting from source
            var newTitle = sourceVideo.Title ?? string.Empty;
            var newDescription = sourceVideo.Description ?? string.Empty;
            var newTags = request.CopyTags ? SanitizeTags(sourceVideo.Tags) : targetVideo.Tags;
            var location = request.CopyLocation
                ? ConvertToLocationTuple(sourceVideo.Location)
                : ConvertToLocationTuple(targetVideo.Location);
            var locationDescription =
                request.CopyLocation ? sourceVideo.LocationDescription : targetVideo.LocationDescription;
            var categoryId = request.CopyCategory ? sourceVideo.CategoryId : targetVideo.CategoryId;
            var defaultLanguage =
                request.CopyDefaultLanguages ? sourceVideo.DefaultLanguage : targetVideo.DefaultLanguage;
            var defaultAudioLanguage = request.CopyDefaultLanguages
                ? sourceVideo.DefaultAudioLanguage
                : targetVideo.DefaultAudioLanguage;

            // Update target video on YouTube. At this point, credits have already been checked/deducted.
            await youTubeIntegration.UpdateVideoAsync(
                request.TargetVideoId,
                newTitle,
                newDescription,
                newTags,
                categoryId,
                defaultLanguage,
                defaultAudioLanguage,
                location,
                locationDescription,
                cancellationToken);

            // Persist changes to DB only after successful YouTube update
            var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
            targetVideo.ApplyDetails(
                newTitle,
                newDescription,
                targetVideo.PublishedAt,
                targetVideo.Duration,
                targetVideo.Visibility,
                newTags,
                categoryId,
                defaultLanguage,
                defaultAudioLanguage,
                ConvertFromLocationTuple(location),
                locationDescription,
                nowUtc,
                null, //we won't know the etag at from this point 
                targetVideo.CommentsAllowed
            );

            await videoRepository.UpsertAsync(channelId, [targetVideo], cancellationToken);

            await userEventLogger.LogAsync(
                userId,
                UserEventType.CopyTemplateExecuted,
                request.TargetVideoId,
                null,
                new
                {
                    sourceVideoId = request.SourceVideoId,
                    copyTags = request.CopyTags,
                    copyLocation = request.CopyLocation,
                    copyPlaylists = request.CopyPlaylists,
                    copyCategory = request.CopyCategory,
                    copyDefaultLanguages = request.CopyDefaultLanguages
                },
                cancellationToken);

            if (!request.CopyPlaylists)
            {
                return new CopyVideoTemplateResult(
                    request.SourceVideoId,
                    request.TargetVideoId,
                    newTitle,
                    newDescription,
                    newTags,
                    locationDescription,
                    location,
                    [],
                    request.CopyCategory,
                    request.CopyDefaultLanguages
                );
            }

            var playlistIds =
                (await playlistRepository.GetPlaylistIdsByVideoAsync(sourceVideo.VideoId, cancellationToken))
                .ToHashSet();
            foreach (var playlistId in playlistIds)
            {
                await youTubeIntegration.AddVideoToPlaylistAsync(playlistId, targetVideo.VideoId,
                    cancellationToken);
            }

            await playlistRepository.SetMembershipsToPlaylistsAsync(targetVideo.VideoId, playlistIds,
                cancellationToken);


            return new CopyVideoTemplateResult(
                request.SourceVideoId,
                request.TargetVideoId,
                newTitle,
                newDescription,
                newTags,
                locationDescription,
                location,
                playlistIds.ToArray(),
                request.CopyCategory,
                request.CopyDefaultLanguages
            );
        }
        catch
        {
            var copyTemplateRefundIdempotencyKey =
                $"copy-template-refund:{userId}:{request.SourceVideoId}:{request.TargetVideoId}:{request.OperationId}";

            await creditsService.RefundAsync(
                userId,
                CreditActionType.CopyTemplateExecuted.ToString(),
                copyTemplateIdempotencyKey,
                copyTemplateRefundIdempotencyKey,
                cancellationToken);

            throw;
        }
    }

    public async Task<VideoDetailsDto?> UpdateVideoMetadataAsync(
        string userId,
        UpdateVideoMetadataRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(request.VideoId))
        {
            return null;
        }

        var channelId = channelContext.GetRequiredChannelId();
        var video = await videoRepository.GetVideoByIdAsync(channelId, request.VideoId, cancellationToken);

        if (video is null)
        {
            return null;
        }

        var title = request.Title?.Trim() ?? string.Empty;
        var description = request.Description ?? string.Empty;
        var tags = SanitizeTags(request.Tags ?? Array.Empty<string>());

        var location = ((double lat, double lng)?)null;
        if (video.Location is not null)
        {
            location = (video.Location.Latitude, video.Location.Longitude);
        }

        var aiTemplateSubmittedIdempotencyKey =
            $"ai-template-submitted:{userId}:{request.VideoId}";

        var aiTemplateSubmittedSpendSucceeded = await creditsService.TrySpendAsync(
            userId,
            CreditActionType.AiTemplateSubmitted.ToString(),
            aiTemplateSubmittedIdempotencyKey,
            request.VideoId,
            new
            {
                generateTitle = !string.IsNullOrWhiteSpace(request.Title),
                generateDescription = request.Description is not null,
                generateTags = request.Tags is not null
            },
            cancellationToken);

        if (!aiTemplateSubmittedSpendSucceeded)
        {
            throw new ForbiddenException(
                "Insufficient credits to submit AI template changes.");
        }

        await youTubeIntegration.UpdateVideoAsync(
            video.VideoId,
            title,
            description,
            tags,
            video.CategoryId,
            video.DefaultLanguage,
            video.DefaultAudioLanguage,
            location,
            video.LocationDescription,
            cancellationToken);

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
        video.ApplyDetails(
            title,
            description,
            video.PublishedAt,
            video.Duration,
            video.Visibility,
            tags,
            video.CategoryId,
            video.DefaultLanguage,
            video.DefaultAudioLanguage,
            video.Location,
            video.LocationDescription,
            nowUtc,
            null,
            video.CommentsAllowed
        );

        await videoRepository.UpsertAsync(channelId, [video], cancellationToken);

        await userEventLogger.LogAsync(
            userId,
            UserEventType.AiTemplateSubmitted,
            request.VideoId,
            null,
            new
            {
                generateTitle = !string.IsNullOrWhiteSpace(request.Title),
                generateDescription = request.Description is not null,
                generateTags = request.Tags is not null
            },
            cancellationToken);

        return new VideoDetailsDto
        {
            Title = title,
            Description = description,
            Tags = tags.ToArray(),
            IsAiTemplateInProgress = video.IsAiTemplateInProgress
        };
    }

    public async Task<VideoDetailsDto?> SaveDraftMetadataAsync(
        string userId,
        UpdateVideoMetadataRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(request.VideoId))
        {
            return null;
        }

        var channelId = channelContext.GetRequiredChannelId();
        var video = await videoRepository.GetVideoByIdAsync(channelId, request.VideoId, cancellationToken);

        if (video is null)
        {
            return null;
        }

        var title = request.Title?.Trim() ?? string.Empty;
        var description = request.Description ?? string.Empty;
        var tags = SanitizeTags(request.Tags ?? Array.Empty<string>());

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
        video.ApplyDetails(
            title,
            description,
            video.PublishedAt,
            video.Duration,
            video.Visibility,
            tags,
            video.CategoryId,
            video.DefaultLanguage,
            video.DefaultAudioLanguage,
            video.Location,
            video.LocationDescription,
            nowUtc,
            null,
            video.CommentsAllowed
        );

        await videoRepository.UpsertAsync(channelId, [video], cancellationToken);

        return new VideoDetailsDto
        {
            Title = title,
            Description = description,
            Tags = tags.ToArray(),
            IsAiTemplateInProgress = video.IsAiTemplateInProgress
        };
    }

    private static string[] SanitizeTags(IReadOnlyList<string> tags)
    {
        var cleanedTags = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<string>();
        var totalCharacters = 0;
        foreach (var tag in cleanedTags)
        {
            var charactersToAdd = tag.Length;
            if (totalCharacters + charactersToAdd > 500)
            {
                break;
            }

            result.Add(tag);
            totalCharacters += charactersToAdd;
        }

        return result.ToArray();
    }

    private static (double lat, double lng)? ConvertToLocationTuple(GeoLocation? location)
    {
        return location is not null
            ? (location.Latitude, location.Longitude)
            : null;
    }

    private static GeoLocation? ConvertFromLocationTuple((double lat, double lng)? location)
    {
        return location.HasValue ? new GeoLocation(location.Value.lat, location.Value.lng) : null;
    }
}