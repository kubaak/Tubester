using System.Net;
using System.Runtime.CompilerServices;
using System.Xml;
using Google;
using Google.Apis.Requests;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions.Auth;
using Tubester.Abstractions.Channels;
using Tubester.Integration.Dtos;

namespace Tubester.Integration;

public sealed class YouTubeIntegration(
    ICurrentUserTokenAccessor currentUserTokenAccessor,
    IYouTubeServiceFactory youTubeServiceFactory,
    ILogger<YouTubeIntegration> logger) : IYouTubeIntegration
{
    public Task<ChannelDto?> GetChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return Task.FromResult<ChannelDto?>(null);
        }

        return ExecuteOrDefaultAsync(
            async () =>
            {
                var youTubeService = await CreateReadOnlyServiceAsync(cancellationToken);

                var channelRequest = youTubeService.Channels.List("snippet,contentDetails");
                channelRequest.Id = channelId;

                var channelResponse = await ExecuteYouTubeRequestAsync(
                    channelRequest,
                    new YouTubeRequestLogContext(
                        Operation: "Channels.List",
                        ChannelId: channelId),
                    cancellationToken);

                var channel = channelResponse.Items?.FirstOrDefault();

                if (channel is null)
                {
                    logger.LogWarning("Channel id '{ChannelId}' not found when fetching details", channelId);
                    return null;
                }

                var uploadsPlaylistId = channel.ContentDetails?.RelatedPlaylists?.Uploads;
                if (string.IsNullOrWhiteSpace(uploadsPlaylistId))
                {
                    logger.LogWarning("Channel '{ChannelId}' has no uploads playlist", channelId);
                    return null;
                }

                return new ChannelDto(
                    channel.Id!,
                    channel.Snippet?.Title ?? channelId,
                    uploadsPlaylistId,
                    channel.ETag);
            },
            fallbackValue: null,
            "Error while getting channel.");
    }

    public async Task<UserChannelDto?> GetCurrentChannelAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new UnauthorizedAccessException(
                "No Google access token is available for the current user. Please sign in again.");
        }

        var youTubeService = youTubeServiceFactory.Create(
            accessToken,
            YouTubeService.Scope.YoutubeReadonly);

        var channelsRequest = youTubeService.Channels.List("snippet,contentDetails");
        channelsRequest.Mine = true;
        channelsRequest.MaxResults = 1;

        var channelsResponse = await ExecuteYouTubeRequestAsync(
            channelsRequest,
            new YouTubeRequestLogContext(
                Operation: "Channels.List.Mine"),
            cancellationToken,
            logGoogleApiErrors: false);

        var channel = channelsResponse.Items?.FirstOrDefault();

        if (channel is null || string.IsNullOrWhiteSpace(channel.Id))
        {
            logger.LogInformation(
                "No current channel found for authenticated user when discovering channel from token");

            return null;
        }

        var title = channel.Snippet?.Title ?? channel.Id;
        var picture = GetBestThumbnailUrl(channel.Snippet?.Thumbnails);
        var uploadsPlaylistId = channel.ContentDetails?.RelatedPlaylists?.Uploads;

        return new UserChannelDto(
            channel.Id,
            title,
            picture,
            uploadsPlaylistId);
    }

    public async IAsyncEnumerable<VideoDto> GetAllVideosAsync(
        string uploadsPlaylistId,
        DateTimeOffset? publishedAfter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var youTubeService = await CreateReadOnlyServiceAsync(cancellationToken);

        string? page = null;

        do
        {
            var playlistRequest = youTubeService.PlaylistItems.List("contentDetails,snippet");
            playlistRequest.PlaylistId = uploadsPlaylistId;
            playlistRequest.MaxResults = 50;
            playlistRequest.PageToken = page;

            var playlistResponse = await ExecuteYouTubeRequestAsync(
                playlistRequest,
                new YouTubeRequestLogContext(
                    Operation: "PlaylistItems.List.Uploads",
                    UploadPlaylistId: uploadsPlaylistId,
                    PlaylistId: uploadsPlaylistId),
                cancellationToken);

            if (playlistResponse.Items is null || playlistResponse.Items.Count == 0)
            {
                yield break;
            }

            if (publishedAfter.HasValue && PageIsNotNewerThan(playlistResponse.Items, publishedAfter.Value))
            {
                yield break;
            }

            var videoIds = new List<string>(playlistResponse.Items.Count);
            var playlistMetadataByVideoId =
                new Dictionary<string, PlaylistVideoMetadata>(StringComparer.Ordinal);

            foreach (var item in playlistResponse.Items)
            {
                var videoId = item.ContentDetails?.VideoId;
                if (string.IsNullOrWhiteSpace(videoId))
                {
                    continue;
                }

                videoIds.Add(videoId);
                playlistMetadataByVideoId[videoId] = new PlaylistVideoMetadata(
                    item.Snippet?.Title,
                    item.Snippet?.Description,
                    item.Snippet?.PublishedAtDateTimeOffset ??
                    item.ContentDetails?.VideoPublishedAtDateTimeOffset);
            }

            if (videoIds.Count == 0)
            {
                page = playlistResponse.NextPageToken;
                continue;
            }

            var videoListRequest = youTubeService.Videos.List("snippet,contentDetails,status,recordingDetails");
            videoListRequest.Id = string.Join(",", videoIds);

            var videoResponse = await ExecuteYouTubeRequestAsync(
                videoListRequest,
                new YouTubeRequestLogContext(
                    Operation: "Videos.List.FromUploads",
                    VideoId: string.Join(",", videoIds),
                    UploadPlaylistId: uploadsPlaylistId,
                    PlaylistId: uploadsPlaylistId),
                cancellationToken);

            foreach (var video in videoResponse.Items ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(video.Id))
                {
                    continue;
                }

                var metadata = playlistMetadataByVideoId[video.Id];

                if (publishedAfter.HasValue &&
                    metadata.PublishedAt.HasValue &&
                    metadata.PublishedAt.Value <= publishedAfter.Value)
                {
                    yield break;
                }

                yield return ToVideoDto(
                    video,
                    titleOverride: metadata.Title,
                    descriptionOverride: metadata.Description,
                    publishedAtOverride: metadata.PublishedAt);
            }

            page = playlistResponse.NextPageToken;
        } while (!string.IsNullOrEmpty(page));
    }

    public async Task<bool?> CheckCommentsAllowedAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        try
        {
            var youTubeService = await CreateReadOnlyServiceAsync(cancellationToken);

            var videoRequest = youTubeService.Videos.List("status");
            videoRequest.Id = videoId;

            var videoResponse = await ExecuteYouTubeRequestAsync(
                videoRequest,
                new YouTubeRequestLogContext(
                    Operation: "Videos.List.Status",
                    VideoId: videoId),
                cancellationToken,
                logGoogleApiErrors: false);

            var video = videoResponse.Items?.FirstOrDefault();

            if (video?.Status?.MadeForKids == true ||
                video?.Status?.SelfDeclaredMadeForKids == true)
            {
                return false;
            }

            var commentsRequest = youTubeService.CommentThreads.List("id");
            commentsRequest.VideoId = videoId;
            commentsRequest.MaxResults = 1;

            await ExecuteYouTubeRequestAsync(
                commentsRequest,
                new YouTubeRequestLogContext(
                    Operation: "CommentThreads.List.CheckCommentsAllowed",
                    VideoId: videoId),
                cancellationToken,
                logGoogleApiErrors: false);

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (GoogleApiException ex) when (IsCommentsDisabled(ex))
        {
            return false;
        }
        catch (GoogleApiException ex)
        {
            logger.LogWarning(
                ex,
                "GoogleApiException checking comments allowed for video {VideoId}. HttpStatusCode={HttpStatusCode}, GoogleReason={GoogleReason}, GoogleLocation={GoogleLocation}",
                videoId,
                ex.HttpStatusCode,
                GetGoogleReason(ex),
                GetGoogleLocation(ex));

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error checking comments allowed for video {VideoId}", videoId);
            return null;
        }
    }

    public async Task<IReadOnlyList<VideoDto>> GetVideosAsync(
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken)
    {
        var videoIdsList = videoIds.ToList();
        if (videoIdsList.Count == 0)
        {
            return Array.Empty<VideoDto>();
        }

        var youTubeService = await CreateReadOnlyServiceAsync(cancellationToken);

        var videoRequest = youTubeService.Videos.List("snippet,contentDetails,status,recordingDetails");
        videoRequest.Id = string.Join(",", videoIdsList);

        var videoResponse = await ExecuteYouTubeRequestAsync(
            videoRequest,
            new YouTubeRequestLogContext(
                Operation: "Videos.List",
                VideoId: string.Join(",", videoIdsList)),
            cancellationToken);

        return videoResponse.Items?
            .Where(video => !string.IsNullOrWhiteSpace(video.Id))
            .Select(video => ToVideoDto(video))
            .ToList() ?? [];
    }

    public async Task<bool> PlaylistContainsVideoAsync(
        string playlistId,
        string videoId,
        CancellationToken cancellationToken)
    {
        var youTubeService = await CreateReadOnlyServiceAsync(cancellationToken);

        var request = youTubeService.PlaylistItems.List("contentDetails");
        request.PlaylistId = playlistId;
        request.VideoId = videoId;
        request.MaxResults = 1;

        var response = await ExecuteYouTubeRequestAsync(
            request,
            new YouTubeRequestLogContext(
                Operation: "PlaylistItems.List.ContainsVideo",
                PlaylistId: playlistId,
                VideoId: videoId),
            cancellationToken);

        return response.Items is { Count: > 0 };
    }

    public async Task ReplyAsync(
        string parentCommentId,
        string text,
        CancellationToken cancellationToken)
    {
        var youTubeService = await CreateWriteServiceAsync(cancellationToken);

        var comment = new Comment
        {
            Snippet = new CommentSnippet
            {
                ParentId = parentCommentId,
                TextOriginal = text
            }
        };

        await ExecuteYouTubeRequestAsync(
            youTubeService.Comments.Insert(comment, "snippet"),
            new YouTubeRequestLogContext(
                Operation: "Comments.Insert",
                ParentCommentId: parentCommentId),
            cancellationToken);
    }

    public async Task UpdateVideoAsync(
        string videoId,
        string title,
        string description,
        IReadOnlyList<string> tags,
        string? categoryId,
        string? defaultLanguage,
        string? defaultAudioLanguage,
        CancellationToken cancellationToken)
    {
        var youTubeService = await CreateWriteServiceAsync(cancellationToken);

        var video = new Video
        {
            Id = videoId,
            Snippet = new VideoSnippet
            {
                Title = title,
                Description = description,
                Tags = tags.ToList(),
                CategoryId = categoryId,
                DefaultLanguage = defaultLanguage,
                DefaultAudioLanguage = defaultAudioLanguage
            }
        };

        await ExecuteYouTubeRequestAsync(
            youTubeService.Videos.Update(video, "snippet"),
            new YouTubeRequestLogContext(
                Operation: "Videos.Update",
                VideoId: videoId),
            cancellationToken);
    }

    public async Task AddVideoToPlaylistAsync(
        string playlistId,
        string videoId,
        CancellationToken cancellationToken)
    {
        var youTubeService = await CreateWriteServiceAsync(cancellationToken);

        string? page = null;
        do
        {
            var listRequest = youTubeService.PlaylistItems.List("contentDetails");
            listRequest.PlaylistId = playlistId;
            listRequest.MaxResults = 50;
            listRequest.PageToken = page;

            var playlistItemsResponse = await ExecuteYouTubeRequestAsync(
                listRequest,
                new YouTubeRequestLogContext(
                    Operation: "PlaylistItems.List.BeforeInsert",
                    VideoId: videoId,
                    PlaylistId: playlistId),
                cancellationToken);

            if (playlistItemsResponse.Items?.Any(item => item.ContentDetails?.VideoId == videoId) == true)
            {
                return;
            }

            page = playlistItemsResponse.NextPageToken;
        } while (!string.IsNullOrEmpty(page));

        var insertRequest = youTubeService.PlaylistItems.Insert(
            new PlaylistItem
            {
                Snippet = new PlaylistItemSnippet
                {
                    PlaylistId = playlistId,
                    ResourceId = new ResourceId
                    {
                        Kind = "youtube#video",
                        VideoId = videoId
                    }
                }
            },
            "snippet");

        await ExecuteYouTubeRequestAsync(
            insertRequest,
            new YouTubeRequestLogContext(
                Operation: "PlaylistItems.Insert",
                VideoId: videoId,
                PlaylistId: playlistId),
            cancellationToken);
    }

    public async IAsyncEnumerable<DetailedPlaylistDto> GetPlaylistsAsync(
        string channelId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var youTubeService = await CreateReadOnlyServiceAsync(cancellationToken);

        string? page = null;

        do
        {
            var playlistRequest = youTubeService.Playlists.List("id,snippet,status");
            playlistRequest.ChannelId = channelId;
            playlistRequest.MaxResults = 50;
            playlistRequest.PageToken = page;

            var playlistResponse = await ExecuteYouTubeRequestAsync(
                playlistRequest,
                new YouTubeRequestLogContext(
                    Operation: "Playlists.List",
                    ChannelId: channelId),
                cancellationToken);

            if (playlistResponse.Items is null || playlistResponse.Items.Count == 0)
            {
                yield break;
            }

            foreach (var playlist in playlistResponse.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(playlist.Id))
                {
                    continue;
                }

                yield return new DetailedPlaylistDto(
                    playlist.Id,
                    playlist.Snippet.Title,
                    playlist.Snippet.Description,
                    playlist.Status?.PrivacyStatus ?? "private",
                    playlist.Snippet.ETag);
            }

            page = playlistResponse.NextPageToken;
        } while (!string.IsNullOrEmpty(page));
    }

    public async IAsyncEnumerable<string> GetPlaylistVideoIdsAsync(
        string playlistId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var youTubeService = await CreateReadOnlyServiceAsync(cancellationToken);

        string? page = null;

        do
        {
            var itemsRequest = youTubeService.PlaylistItems.List("contentDetails");
            itemsRequest.PlaylistId = playlistId;
            itemsRequest.MaxResults = 50;
            itemsRequest.PageToken = page;

            PlaylistItemListResponse itemsResponse;

            try
            {
                itemsResponse = await ExecuteYouTubeRequestAsync(
                    itemsRequest,
                    new YouTubeRequestLogContext(
                        Operation: "PlaylistItems.List.VideoIds",
                        PlaylistId: playlistId),
                    cancellationToken,
                    logGoogleApiErrors: false);
            }
            catch (GoogleApiException ex) when (IsPlaylistNotFound(ex))
            {
                logger.LogWarning(
                    ex,
                    "YouTube playlist was not found when loading playlist video ids. Treating it as empty. PlaylistId={PlaylistId}, HttpStatusCode={HttpStatusCode}, GoogleReason={GoogleReason}, GoogleLocation={GoogleLocation}",
                    playlistId,
                    ex.HttpStatusCode,
                    GetGoogleReason(ex),
                    GetGoogleLocation(ex));

                yield break;
            }

            if (itemsResponse.Items is null || itemsResponse.Items.Count == 0)
            {
                yield break;
            }

            foreach (var item in itemsResponse.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var videoId = item.ContentDetails?.VideoId;
                if (!string.IsNullOrWhiteSpace(videoId))
                {
                    yield return videoId;
                }
            }

            page = itemsResponse.NextPageToken;
        } while (!string.IsNullOrEmpty(page));
    }

    private static bool IsPlaylistNotFound(GoogleApiException ex)
    {
        return ex.HttpStatusCode == HttpStatusCode.NotFound &&
               ex.Error?.Errors?.Any(error =>
                   string.Equals(error.Reason, "playlistNotFound", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private async Task<YouTubeService> CreateReadOnlyServiceAsync(CancellationToken cancellationToken)
    {
        return youTubeServiceFactory.Create(
            await GetCurrentUsersAccessToken(cancellationToken),
            YouTubeService.Scope.YoutubeReadonly);
    }

    private async Task<YouTubeService> CreateWriteServiceAsync(CancellationToken cancellationToken)
    {
        return youTubeServiceFactory.Create(
            await GetCurrentUsersAccessToken(cancellationToken),
            YouTubeService.Scope.YoutubeForceSsl);
    }

    private async Task<string> GetCurrentUsersAccessToken(CancellationToken cancellationToken)
    {
        var accessToken = await currentUserTokenAccessor.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new UnauthorizedAccessException(
                "No Google access token is available for the current user. Please sign in again.");
        }

        return accessToken;
    }

    private async Task<T> ExecuteOrDefaultAsync<T>(
        Func<Task<T>> action,
        T fallbackValue,
        string errorMessage)
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (GoogleApiException)
        {
            return fallbackValue;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected YouTube integration error. {ErrorMessage}", errorMessage);
            return fallbackValue;
        }
    }

    private async Task<TResponse> ExecuteYouTubeRequestAsync<TResponse>(
        ClientServiceRequest<TResponse> request,
        YouTubeRequestLogContext context,
        CancellationToken cancellationToken,
        bool logGoogleApiErrors = true)
    {
        try
        {
            return await request.ExecuteAsync(cancellationToken);
        }
        catch (GoogleApiException ex) when (IsUnauthorized(ex))
        {
            logger.LogWarning(
                ex,
                "Unauthorized YouTube API request while executing {YouTubeOperation}. ChannelId={ChannelId}, VideoId={VideoId}, UploadPlaylistId={UploadPlaylistId}, PlaylistId={PlaylistId}, ParentCommentId={ParentCommentId}, HttpStatusCode={HttpStatusCode}, GoogleReason={GoogleReason}, GoogleLocation={GoogleLocation}",
                context.Operation,
                context.ChannelId,
                context.VideoId,
                context.UploadPlaylistId,
                context.PlaylistId,
                context.ParentCommentId,
                ex.HttpStatusCode,
                GetGoogleReason(ex),
                GetGoogleLocation(ex));

            throw new UnauthorizedAccessException(
                "Your Google session has expired. Please sign in again.",
                ex);
        }
        catch (GoogleApiException ex)
        {
            if (logGoogleApiErrors)
            {
                logger.LogError(
                    ex,
                    "YouTube API error while executing {YouTubeOperation}. ChannelId={ChannelId}, VideoId={VideoId}, UploadPlaylistId={UploadPlaylistId}, PlaylistId={PlaylistId}, ParentCommentId={ParentCommentId}, HttpStatusCode={HttpStatusCode}, GoogleReason={GoogleReason}, GoogleLocation={GoogleLocation}",
                    context.Operation,
                    context.ChannelId,
                    context.VideoId,
                    context.UploadPlaylistId,
                    context.PlaylistId,
                    context.ParentCommentId,
                    ex.HttpStatusCode,
                    GetGoogleReason(ex),
                    GetGoogleLocation(ex));
            }

            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Unexpected YouTube integration error while executing {YouTubeOperation}. ChannelId={ChannelId}, VideoId={VideoId}, UploadPlaylistId={UploadPlaylistId}, PlaylistId={PlaylistId}, ParentCommentId={ParentCommentId}",
                context.Operation,
                context.ChannelId,
                context.VideoId,
                context.UploadPlaylistId,
                context.PlaylistId,
                context.ParentCommentId);

            throw;
        }
    }

    private static VideoDto ToVideoDto(
        Video video,
        string? titleOverride = null,
        string? descriptionOverride = null,
        DateTimeOffset? publishedAtOverride = null)
    {
        var duration = XmlConvert.ToTimeSpan(video.ContentDetails?.Duration ?? "PT0S");
        var privacyStatus = GetPrivacyStatus(video.Status);

        return new VideoDto(
            video.Id!,
            titleOverride ?? video.Snippet?.Title ?? string.Empty,
            descriptionOverride ?? video.Snippet?.Description ?? string.Empty,
            video.Snippet?.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)),
            duration,
            privacyStatus,
            duration <= TimeSpan.FromSeconds(60),
            publishedAtOverride ?? video.Snippet?.PublishedAtDateTimeOffset ?? DateTimeOffset.MinValue,
            video.Snippet?.CategoryId,
            video.Snippet?.DefaultLanguage,
            video.Snippet?.DefaultAudioLanguage,
            video.ETag,
            null);
    }

    private static string GetPrivacyStatus(VideoStatus? status)
    {
        var privacy = status?.PrivacyStatus ?? "private";
        var publishAt = status?.PublishAtDateTimeOffset;

        var isScheduled = string.Equals(privacy, "private", StringComparison.OrdinalIgnoreCase)
                          && publishAt.HasValue
                          && publishAt.Value > DateTimeOffset.UtcNow;

        return isScheduled ? "scheduled" : privacy;
    }

    private static string? GetBestThumbnailUrl(ThumbnailDetails? thumbnails)
    {
        if (thumbnails is null)
        {
            return null;
        }

        var candidates = new[]
        {
            thumbnails.Maxres,
            thumbnails.Standard,
            thumbnails.High,
            thumbnails.Medium,
            thumbnails.Default__
        };

        return candidates
            .Select(thumbnail => thumbnail?.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }

    private static bool PageIsNotNewerThan(
        IList<PlaylistItem> items,
        DateTimeOffset publishedAfter)
    {
        var firstItem = items.FirstOrDefault();
        var newest = firstItem?.ContentDetails?.VideoPublishedAtDateTimeOffset ??
                     firstItem?.Snippet?.PublishedAtDateTimeOffset;

        return newest.HasValue && newest.Value <= publishedAfter;
    }

    private static bool IsUnauthorized(GoogleApiException ex)
    {
        return ex.HttpStatusCode == HttpStatusCode.Unauthorized;
    }

    private static bool IsCommentsDisabled(GoogleApiException ex)
    {
        return ex.HttpStatusCode == HttpStatusCode.Forbidden &&
               ex.Error?.Errors?.Any(error => error.Reason == "commentsDisabled") == true;
    }

    private static string? GetGoogleReason(GoogleApiException ex)
    {
        return ex.Error?.Errors?.FirstOrDefault()?.Reason;
    }

    private static string? GetGoogleLocation(GoogleApiException ex)
    {
        return ex.Error?.Errors?.FirstOrDefault()?.Location;
    }

    private sealed record YouTubeRequestLogContext(
        string Operation,
        string? ChannelId = null,
        string? VideoId = null,
        string? UploadPlaylistId = null,
        string? PlaylistId = null,
        string? ParentCommentId = null);

    private sealed record PlaylistVideoMetadata(
        string? Title,
        string? Description,
        DateTimeOffset? PublishedAt);
}