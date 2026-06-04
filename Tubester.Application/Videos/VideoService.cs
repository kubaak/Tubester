using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Abstractions;
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
    IDateTimeOffsetProvider dateTimeOffsetProvider,
    ILogger<VideoService> logger) : IVideoService
{
    public async Task<PagedResult<VideoListItemDto>> GetVideosAsync(GetVideosRequest request, CancellationToken ct)
    {
        var title = request.Title;
        var visibility = request.Visibility;
        var pageSize = request.PageSize;
        var pageToken = request.PageToken;

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
            if (!PageToken.TryParse(pageToken, out var publishedAt, out var videoId, out var tokenBinding))
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

        var uploadPlaylistId = channelContext.GetRequiredUploadPlaylistId();

        // Fetch one extra item to determine if there's a next page
        var take = effectivePageSize + 1;
        var videos =
            await videoRepository.GetVideosPageAsync(uploadPlaylistId, normalizedTitle, visibility, afterPublishedAtUtc,
                afterVideoId,
                take, ct);

        // Determine if there are more items
        var hasMore = videos.Count > effectivePageSize;
        var itemsToReturn = hasMore ? videos.Take(effectivePageSize).ToList() : videos;

        string? nextPageToken = null;
        if (hasMore && itemsToReturn.Count > 0)
        {
            var lastItem = itemsToReturn[^1];
            nextPageToken = PageToken.Serialize(lastItem.PublishedAt, lastItem.VideoId, binding);
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

        var uploadPlaylistId = channelContext.GetRequiredUploadPlaylistId();
        var video = await videoRepository.GetVideoByIdAsync(uploadPlaylistId, videoId, cancellationToken);

        if (video is null)
        {
            return null;
        }

        var playlists = await playlistRepository.GetPlaylistsByVideoAsync(videoId, cancellationToken);

        return new VideoDetailsDto
        {
            Title = video.Title,
            Description = video.Description,
            Tags = video.Tags,
            IsAiTitleInProgress = video.IsAiTitleInProgress,
            IsAiDescriptionInProgress = video.IsAiDescriptionInProgress,
            IsAiTagsInProgress = video.IsAiTagsInProgress,
            IsAiPlaylistSuggestionInProgress = video.IsAiPlaylistSuggestionInProgress,
            Playlists = [.. playlists.Select(p => new PlaylistDto { Id = p.PlaylistId, Name = p.Title })],
            Category = video.CategoryId is { } catId ? new CategoryDto(catId, null) : null,
            DefaultLanguage = video.DefaultLanguage,
            DefaultAudioLanguage = video.DefaultAudioLanguage,
            ThumbnailUrl = video.ThumbnailUrl
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

        var uploadPlaylistId = channelContext.GetRequiredUploadPlaylistId();

        // Load source and target videos from DB
        var sourceVideo =
            await videoRepository.GetVideoByIdAsync(uploadPlaylistId, request.SourceVideoId, cancellationToken)
            ?? throw new ArgumentException($"Source video {request.SourceVideoId} not found in cache.");

        var targetVideo =
            await videoRepository.GetVideoByIdAsync(uploadPlaylistId, request.TargetVideoId, cancellationToken)
            ?? throw new ArgumentException($"Target video {request.TargetVideoId} not found in cache.");

        // Build effective metadata starting from source
        var newTitle = request.CopyTitle ? (sourceVideo.Title ?? string.Empty) : (targetVideo.Title ?? string.Empty);
        var newDescription = request.CopyDescription ? (sourceVideo.Description ?? string.Empty) : (targetVideo.Description ?? string.Empty);
        var newTags = request.CopyTags ? SanitizeTags(sourceVideo.Tags) : targetVideo.Tags;
        var categoryId = request.CopyCategory ? sourceVideo.CategoryId : targetVideo.CategoryId;
        var defaultLanguage =
            request.CopyDefaultLanguages ? sourceVideo.DefaultLanguage : targetVideo.DefaultLanguage;
        var defaultAudioLanguage = request.CopyDefaultLanguages
            ? sourceVideo.DefaultAudioLanguage
            : targetVideo.DefaultAudioLanguage;

        // Persist changes to DB
        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
        targetVideo.ApplyLocalChanges(
            newTitle,
            newDescription,
            targetVideo.PublishedAt,
            targetVideo.Duration,
            targetVideo.Visibility,
            newTags,
            categoryId,
            defaultLanguage,
            defaultAudioLanguage,
            nowUtc,
            null, //we won't know the etag at from this point 
            targetVideo.CommentsAllowed
        );

        await videoRepository.UpdateExistingAsync(uploadPlaylistId, [targetVideo],
            static (existingVideo, incomingVideo, nowUtc) => existingVideo.ApplyLocalChanges(
                incomingVideo.Title,
                incomingVideo.Description,
                incomingVideo.PublishedAt,
                incomingVideo.Duration,
                incomingVideo.Visibility,
                incomingVideo.Tags,
                incomingVideo.CategoryId,
                incomingVideo.DefaultLanguage,
                incomingVideo.DefaultAudioLanguage,
                nowUtc,
                incomingVideo.ETag,
                incomingVideo.CommentsAllowed),
            cancellationToken);

        await userEventLogger.LogAsync(
            userId,
            UserEventType.CopyTemplateExecuted,
            request.TargetVideoId,
            null,
            new
            {
                sourceVideoId = request.SourceVideoId,
                copyTags = request.CopyTags,
                copyTitle = request.CopyTitle,
                copyDescription = request.CopyDescription,
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
                [],
                request.CopyTitle,
                request.CopyDescription,
                request.CopyCategory,
                request.CopyDefaultLanguages
            );
        }

        var playlistIds =
            (await playlistRepository.GetPlaylistIdsByVideoAsync(sourceVideo.VideoId, cancellationToken))
            .ToHashSet();

        await playlistRepository.SetMembershipsToPlaylistsAsync(targetVideo.VideoId, playlistIds,
            cancellationToken);


        return new CopyVideoTemplateResult(
            request.SourceVideoId,
            request.TargetVideoId,
            newTitle,
            newDescription,
            newTags,
            playlistIds.ToArray(),
            request.CopyTitle,
            request.CopyDescription,
            request.CopyCategory,
            request.CopyDefaultLanguages
        );
    }

    public async Task<VideoDetailsDto?> SaveDraftMetadataAsync(
        string userId,
        SaveVideoDraftRequest request,
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

        var uploadPlaylistId = channelContext.GetRequiredUploadPlaylistId();
        var video = await videoRepository.GetVideoByIdAsync(uploadPlaylistId, request.VideoId, cancellationToken);

        if (video is null)
        {
            return null;
        }

        var title = request.Title?.Trim() ?? string.Empty;
        var description = request.Description ?? string.Empty;
        var tags = SanitizeTags(request.Tags ?? Array.Empty<string>());

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        video.ApplyLocalChanges(
            title,
            description,
            video.PublishedAt,
            video.Duration,
            video.Visibility,
            tags,
            video.CategoryId,
            video.DefaultLanguage,
            video.DefaultAudioLanguage,
            nowUtc,
            null,
            video.CommentsAllowed
        );

        await videoRepository.UpdateExistingAsync(uploadPlaylistId, [video],
            static (existingVideo, incomingVideo, nowUtc) => existingVideo.ApplyLocalChanges(
                incomingVideo.Title,
                incomingVideo.Description,
                incomingVideo.PublishedAt,
                incomingVideo.Duration,
                incomingVideo.Visibility,
                incomingVideo.Tags,
                incomingVideo.CategoryId,
                incomingVideo.DefaultLanguage,
                incomingVideo.DefaultAudioLanguage,
                nowUtc,
                incomingVideo.ETag,
                incomingVideo.CommentsAllowed),
            cancellationToken);

        // Update video playlist memberships if playlistIds provided
        if (request.PlaylistIds is not null)
        {
            var playlistIdSet = request.PlaylistIds.ToHashSet(StringComparer.Ordinal);
            await playlistRepository.SetMembershipsToPlaylistsAsync(video.VideoId, playlistIdSet, cancellationToken);
        }

        var draftPlaylists = await playlistRepository.GetPlaylistsByVideoAsync(request.VideoId, cancellationToken);

        return new VideoDetailsDto
        {
            Title = title,
            Description = description,
            Tags = tags.ToArray(),
            IsAiTitleInProgress = video.IsAiTitleInProgress,
            IsAiDescriptionInProgress = video.IsAiDescriptionInProgress,
            IsAiTagsInProgress = video.IsAiTagsInProgress,
            IsAiPlaylistSuggestionInProgress = video.IsAiPlaylistSuggestionInProgress,
            Playlists = [.. draftPlaylists.Select(p => new PlaylistDto { Id = p.PlaylistId, Name = p.Title })],
            Category = video.CategoryId is { } draftCatId ? new CategoryDto(draftCatId, null) : null,
            DefaultLanguage = video.DefaultLanguage,
            DefaultAudioLanguage = video.DefaultAudioLanguage,
            ThumbnailUrl = video.ThumbnailUrl
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

    public async Task<VideoDetailsDto?> UpdateVideoAsync(
    string userId,
    string operationId,
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

        var uploadPlaylistId = channelContext.GetRequiredUploadPlaylistId();
        var video = await videoRepository.GetVideoByIdAsync(uploadPlaylistId, request.VideoId, cancellationToken);

        if (video is null)
        {
            return null;
        }

        var aiTemplateSubmittedIdempotencyKey =
            $"ai-template-submitted:{userId}:{request.VideoId}:{operationId}";

        var spendResult = await creditsService.TrySpendAsync(
            userId,
            nameof(CreditActionType.AiTemplateSubmitted),
            aiTemplateSubmittedIdempotencyKey,
            request.VideoId,
            new
            {
                generateTitle = false,
                generateDescription = false,
                generateTags = false
            },
            cancellationToken);

        if (!spendResult.Succeeded)
        {
            throw new ForbiddenException(
                "Insufficient credits to submit AI template changes.");
        }

        var playlists = await playlistRepository.GetPlaylistsByVideoAsync(request.VideoId, cancellationToken);

        await youTubeIntegration.UpdateVideoAsync(
            video.VideoId,
            video.Title ?? string.Empty,
            video.Description ?? string.Empty,
            video.Tags,
            video.CategoryId,
            video.DefaultLanguage,
            video.DefaultAudioLanguage,
            cancellationToken);

        if (playlists.Count > 0)
        {
            var tasks = playlists.Select(p => youTubeIntegration.AddVideoToPlaylistAsync(p.PlaylistId, video.VideoId, cancellationToken));
            await Task.WhenAll(tasks);
        }
        
        await videoRepository.UpdateExistingAsync(uploadPlaylistId, [video], 
            static(v, _, nowUtc) =>
            {
                v.MarkAsClean(nowUtc);
                return true;
            }, cancellationToken);

        return new VideoDetailsDto
        {
            Title = video.Title,
            Description = video.Description,
            Tags = video.Tags,
            IsAiTitleInProgress = video.IsAiTitleInProgress,
            IsAiDescriptionInProgress = video.IsAiDescriptionInProgress,
            IsAiTagsInProgress = video.IsAiTagsInProgress,
            IsAiPlaylistSuggestionInProgress = video.IsAiPlaylistSuggestionInProgress,
            Playlists = [.. playlists.Select(p => new PlaylistDto { Id = p.PlaylistId, Name = p.Title })],
            Category = video.CategoryId is { } catId ? new CategoryDto(catId, null) : null,
            DefaultLanguage = video.DefaultLanguage,
            DefaultAudioLanguage = video.DefaultAudioLanguage,
            ThumbnailUrl = video.ThumbnailUrl
        };
    }

    public async Task<VideoDetailsDto?> ResyncVideoAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        var uploadPlaylistId = channelContext.GetRequiredUploadPlaylistId();
        var channelId = channelContext.GetRequiredChannelId();

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            { LoggingConstants.ChannelId, channelId },
            { LoggingConstants.UploadPlaylistId, uploadPlaylistId },
            { LoggingConstants.VideoId, videoId },
        });

        var video = await videoRepository.GetVideoByIdAsync(
            uploadPlaylistId,
            videoId,
            cancellationToken);

        if (video is null)
        {
            return null;
        }

        var videoDtos = await youTubeIntegration.GetVideosAsync([videoId], cancellationToken);
        var videoDto = videoDtos.FirstOrDefault();

        if (videoDto is null)
        {
            return null;
        }

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        var visibility = videoDto.PrivacyStatus.ToLowerInvariant() switch
        {
            "public" => VideoVisibility.Public,
            "unlisted" => VideoVisibility.Unlisted,
            "private" => VideoVisibility.Private,
            "scheduled" => VideoVisibility.Scheduled,
            _ => VideoVisibility.Private
        };

        video.OverrideFromRemote(
            videoDto.Title,
            videoDto.Description,
            videoDto.PublishedAt,
            videoDto.Duration,
            visibility,
            videoDto.Tags,
            videoDto.CategoryId,
            videoDto.DefaultLanguage,
            videoDto.DefaultAudioLanguage,
            nowUtc,
            videoDto.ETag,
            videoDto.CommentsAllowed);

        await videoRepository.UpdateExistingAsync(
            uploadPlaylistId,
            [video],
            static (existingVideo, incomingVideo, nowUtc) => existingVideo.OverrideFromRemote(
                incomingVideo.Title,
                incomingVideo.Description,
                incomingVideo.PublishedAt,
                incomingVideo.Duration,
                incomingVideo.Visibility,
                incomingVideo.Tags,
                incomingVideo.CategoryId,
                incomingVideo.DefaultLanguage,
                incomingVideo.DefaultAudioLanguage,
                nowUtc,
                incomingVideo.ETag,
                incomingVideo.CommentsAllowed),
            cancellationToken);

        // Important: await this normally. Do not start it earlier as a Task while
        // another repository method may still be using the same DbContext.
        var channelPlaylists = await playlistRepository.GetByChannelAsync(
            channelId,
            cancellationToken);

        // This is safe to parallelize because these are YouTube API calls,
        // not EF DbContext calls.
        var playlistMembershipTasks = channelPlaylists.Select(async playlist =>
        {
            using var playlistScope = logger.BeginScope(new Dictionary<string, object?>
            {
                { LoggingConstants.PlaylistId, playlist.PlaylistId }
            });

            var containsVideo = await youTubeIntegration.PlaylistContainsVideoAsync(
                playlist.PlaylistId,
                videoId,
                cancellationToken);

            return containsVideo ? playlist.PlaylistId : null;
        });

        var playlistIds = await Task.WhenAll(playlistMembershipTasks);

        var updatedPlaylistIds = playlistIds
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        await playlistRepository.SetMembershipsToPlaylistsAsync(
            videoId,
            updatedPlaylistIds,
            cancellationToken);

        var playlists = await playlistRepository.GetPlaylistsByVideoAsync(
            videoId,
            cancellationToken);

        return new VideoDetailsDto
        {
            Title = video.Title,
            Description = video.Description,
            Tags = video.Tags,
            IsAiTitleInProgress = video.IsAiTitleInProgress,
            IsAiDescriptionInProgress = video.IsAiDescriptionInProgress,
            IsAiTagsInProgress = video.IsAiTagsInProgress,
            IsAiPlaylistSuggestionInProgress = video.IsAiPlaylistSuggestionInProgress,
            Playlists =
            [
                ..playlists.Select(p => new PlaylistDto
                {
                    Id = p.PlaylistId,
                    Name = p.Title
                })
            ],
            Category = video.CategoryId is { } catId ? new CategoryDto(catId, null) : null,
            DefaultLanguage = video.DefaultLanguage,
            DefaultAudioLanguage = video.DefaultAudioLanguage,
            ThumbnailUrl = video.ThumbnailUrl
        };
    }
    
    public Task<List<VideoListItemDto>> GetDirtyVideosAsync(CancellationToken cancellationToken)
    {
        var uploadPlaylistId = channelContext.GetRequiredUploadPlaylistId();
        return videoRepository.GetDirtyVideosAsync(uploadPlaylistId, cancellationToken);
    }
}