using System.Net;
using System.Runtime.CompilerServices;
using System.Xml;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions.Auth;
using Tubester.Abstractions.Channels;
using Tubester.Integration.Dtos;

namespace Tubester.Integration;

public sealed class YouTubeIntegration(
    ICurrentUserTokenAccessor currentUserTokenAccessor,
    ILogger<YouTubeIntegration> logger) : IYouTubeIntegration
{
    public async Task<ChannelDto?> GetChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return null;
        }

        try
        {
            var youTubeService = CreateReadOnlyServiceAsync(await GetCurrentUsersAccessToken(cancellationToken));

            // Fetch channel details by id to get uploads playlist + title + ETag
            var channelRequest = youTubeService.Channels.List("snippet,contentDetails");
            channelRequest.Id = channelId;

            var channelResponse = await channelRequest.ExecuteAsync(cancellationToken);
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

            var title = channel.Snippet?.Title ?? channelId;
            var etag = channel.ETag;

            return new ChannelDto(
                channel.Id!,
                title,
                uploadsPlaylistId,
                etag
            );
        }
        catch (GoogleApiException ex)
        {
            logger.LogError(ex, "YouTube API error while getting channel for id '{ChannelId}'", channelId);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while getting channel for id '{ChannelId}'", channelId);
            return null;
        }
    }

    public async Task<UserChannelDto?> GetCurrentChannelAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        try
        {
            var youTubeService = CreateReadOnlyServiceAsync(accessToken);

            var channelsRequest = youTubeService.Channels.List("snippet");
            channelsRequest.Mine = true;
            channelsRequest.MaxResults = 1;

            var channelsResponse = await channelsRequest.ExecuteAsync(cancellationToken);
            var channel = channelsResponse.Items?.FirstOrDefault();
            if (channel is null || string.IsNullOrWhiteSpace(channel.Id))
            {
                logger.LogInformation(
                    "No current channel found for authenticated user when discovering channel from token.");
                return null;
            }

            var title = channel.Snippet?.Title ?? channel.Id;

            string? picture = null;
            var thumbnails = channel.Snippet?.Thumbnails;
            if (thumbnails is not null)
            {
                var thumbnailCandidates = new[]
                {
                    thumbnails.Maxres, thumbnails.Standard, thumbnails.High, thumbnails.Medium, thumbnails.Default__
                };

                foreach (var thumbnail in thumbnailCandidates)
                {
                    if (!string.IsNullOrWhiteSpace(thumbnail?.Url))
                    {
                        picture = thumbnail.Url;
                        break;
                    }
                }
            }

            return new UserChannelDto(
                channel.Id,
                title,
                picture
            );
        }
        catch (GoogleApiException ex)
        {
            logger.LogError(ex, "YouTube API error while discovering current channel from access token.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while discovering current channel from access token.");
            return null;
        }
    }

    public async Task<IReadOnlyList<ChannelDto>> GetUserChannelsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var youTubeService = CreateReadOnlyServiceAsync(await GetCurrentUsersAccessToken(cancellationToken));

            var channels = new List<ChannelDto>();
            string? pageToken = null;

            do
            {
                var channelsRequest = youTubeService.Channels.List("snippet,contentDetails");
                channelsRequest.Mine = true;
                channelsRequest.MaxResults = 50;
                channelsRequest.PageToken = pageToken;

                var channelsResponse = await channelsRequest.ExecuteAsync(cancellationToken);
                if (channelsResponse.Items is null || channelsResponse.Items.Count == 0)
                {
                    break;
                }

                foreach (var channel in channelsResponse.Items)
                {
                    if (string.IsNullOrWhiteSpace(channel.Id))
                    {
                        continue;
                    }

                    var uploadsPlaylistId = channel.ContentDetails?.RelatedPlaylists?.Uploads;
                    if (string.IsNullOrWhiteSpace(uploadsPlaylistId))
                    {
                        continue;
                    }

                    var title = channel.Snippet?.Title ?? channel.Id;
                    var etag = channel.ETag;

                    channels.Add(new ChannelDto(
                        channel.Id,
                        title,
                        uploadsPlaylistId,
                        etag
                    ));
                }

                pageToken = channelsResponse.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));

            return channels;
        }
        catch (GoogleApiException ex)
        {
            logger.LogError(ex, "YouTube API error while getting channels for current user.");
            return Array.Empty<ChannelDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while getting channels for current user.");
            return Array.Empty<ChannelDto>();
        }
    }

    public async IAsyncEnumerable<VideoDto> GetAllVideosAsync(
        string uploadsPlaylistId,
        DateTimeOffset? publishedAfter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var youTubeService = CreateReadOnlyServiceAsync(await GetCurrentUsersAccessToken(cancellationToken));

        string? page = null;

        do
        {
            var playlistRequest = youTubeService.PlaylistItems.List("contentDetails,snippet");
            playlistRequest.PlaylistId = uploadsPlaylistId;
            playlistRequest.MaxResults = 50;
            playlistRequest.PageToken = page;

            var playlistResponse = await playlistRequest.ExecuteAsync(cancellationToken);
            if (playlistResponse.Items is null || playlistResponse.Items.Count == 0)
            {
                yield break;
            }

            if (publishedAfter.HasValue)
            {
                var firstItem = playlistResponse.Items.First();
                var newest = firstItem.ContentDetails?.VideoPublishedAtDateTimeOffset ??
                             firstItem.Snippet?.PublishedAtDateTimeOffset;
                if (newest.HasValue && newest.Value <= publishedAfter.Value)
                {
                    yield break;
                }
            }

            var videoIds = new List<string>(playlistResponse.Items.Count);
            var metaDictionary =
                new Dictionary<string, (string? Title, string? Description, DateTimeOffset? PublishedAt)>(StringComparer
                    .Ordinal);

            foreach (var item in playlistResponse.Items)
            {
                var videoId = item.ContentDetails?.VideoId;
                if (string.IsNullOrWhiteSpace(videoId))
                {
                    continue;
                }

                videoIds.Add(videoId);
                metaDictionary[videoId] = (
                    item.Snippet?.Title,
                    item.Snippet?.Description,
                    item.Snippet?.PublishedAtDateTimeOffset ?? item.ContentDetails?.VideoPublishedAtDateTimeOffset
                );
            }

            if (videoIds.Count == 0)
            {
                page = playlistResponse.NextPageToken;
                continue;
            }

            var videoListRequest = youTubeService.Videos.List("snippet,contentDetails,status,recordingDetails");
            videoListRequest.Id = string.Join(",", videoIds);
            var videoResponse = await videoListRequest.ExecuteAsync(cancellationToken);

            foreach (var video in videoResponse.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(video.Id))
                {
                    continue;
                }

                var (metaTitle, metaDescription, metaPublishedAt) = metaDictionary[video.Id];
                if (publishedAfter.HasValue && metaPublishedAt.HasValue &&
                    metaPublishedAt.Value <= publishedAfter.Value)
                {
                    yield break;
                }

                var title = metaTitle ?? video.Snippet.Title ?? string.Empty;
                var description = metaDescription ?? video.Snippet.Title ?? string.Empty;

                var tags = video.Snippet?.Tags?
                    .Where(t => !string.IsNullOrWhiteSpace(t));

                var iso = video.ContentDetails?.Duration ?? "PT0S";
                var duration = XmlConvert.ToTimeSpan(iso);

                var privacy = video.Status?.PrivacyStatus ?? "private";
                var publishAt = video.Status?.PublishAtDateTimeOffset;
                var isScheduled = string.Equals(privacy, "private", StringComparison.OrdinalIgnoreCase)
                                  && publishAt.HasValue
                                  && publishAt.Value > DateTimeOffset.UtcNow;

                var privacyStatus = isScheduled ? "scheduled" : privacy;

                var geoPoint = video.RecordingDetails?.Location;
                var latitude = geoPoint?.Latitude;
                var longitude = geoPoint?.Longitude;
                ValueTuple<double, double>? location = null;
                if (latitude is not null && longitude is not null)
                {
                    location = new ValueTuple<double, double>(latitude.Value, longitude.Value);
                }

                yield return new VideoDto(
                    video.Id, title, description, tags, duration, privacyStatus,
                    duration <= TimeSpan.FromSeconds(60), metaPublishedAt ?? DateTimeOffset.MinValue,
                    video.Snippet?.CategoryId, video.Snippet?.DefaultLanguage, video.Snippet?.DefaultAudioLanguage,
                    location, video.RecordingDetails?.LocationDescription,
                    video.ETag, null // CommentsAllowed will be determined separately
                );
            }

            page = playlistResponse.NextPageToken;
        } while (!string.IsNullOrEmpty(page));
    }

    public async Task<bool?> CheckCommentsAllowedAsync(string videoId,
        CancellationToken cancellationToken)
    {
        try
        {
            var youTubeService = CreateReadOnlyServiceAsync(await GetCurrentUsersAccessToken(cancellationToken));

            // First check if video is made for kids (short-circuit)
            var videoRequest = youTubeService.Videos.List("status");
            videoRequest.Id = videoId;
            var videoResponse = await videoRequest.ExecuteAsync(cancellationToken);
            var video = videoResponse.Items?.FirstOrDefault();

            if (video?.Status != null)
            {
                if (video.Status.MadeForKids == true || video.Status.SelfDeclaredMadeForKids == true)
                {
                    return false; // Comments disabled for kids content
                }
            }

            // Check comments by attempting to list them
            var commentsRequest = youTubeService.CommentThreads.List("id");
            commentsRequest.VideoId = videoId;
            commentsRequest.MaxResults = 1;

            await commentsRequest.ExecuteAsync(cancellationToken);
            return true; // Comments allowed if request succeeds
        }
        catch (GoogleApiException ex)
        {
            // Check if the error is specifically about comments being disabled
            if (ex.HttpStatusCode == HttpStatusCode.Forbidden &&
                ex.Error?.Errors?.Any(e => e.Reason == "commentsDisabled") == true)
            {
                return false;
            }

            logger.LogWarning(ex, "GoogleApiException Checking comments allowed for video {VideoId}", videoId);
            // For other errors, return null to indicate unknown status
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Checking comments allowed for video {VideoId}", videoId);
            // For any other exceptions, return null to indicate unknown status
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

        var youTubeService = CreateReadOnlyServiceAsync(await GetCurrentUsersAccessToken(cancellationToken));

        var videoRequest = youTubeService.Videos.List("snippet,contentDetails,status,recordingDetails");
        videoRequest.Id = string.Join(",", videoIdsList);

        var videoResponse = await videoRequest.ExecuteAsync(cancellationToken);
        var result = new List<VideoDto>();

        foreach (var video in videoResponse.Items)
        {
            if (string.IsNullOrWhiteSpace(video.Id))
            {
                continue;
            }

            var title = video.Snippet?.Title ?? string.Empty;
            var description = video.Snippet?.Description ?? string.Empty;
            var tags = video.Snippet?.Tags?.Where(t => !string.IsNullOrWhiteSpace(t));
            var iso = video.ContentDetails?.Duration ?? "PT0S";
            var duration = XmlConvert.ToTimeSpan(iso);
            var privacy = video.Status?.PrivacyStatus ?? "private";
            var publishAt = video.Status?.PublishAtDateTimeOffset;
            var isScheduled = string.Equals(privacy, "private", StringComparison.OrdinalIgnoreCase)
                              && publishAt.HasValue
                              && publishAt.Value > DateTimeOffset.UtcNow;
            var privacyStatus = isScheduled ? "scheduled" : privacy;
            var geoPoint = video.RecordingDetails?.Location;
            var latitude = geoPoint?.Latitude;
            var longitude = geoPoint?.Longitude;
            ValueTuple<double, double>? location = null;
            if (latitude is not null && longitude is not null)
            {
                location = new ValueTuple<double, double>(latitude.Value, longitude.Value);
            }

            result.Add(new VideoDto(
                video.Id, title, description, tags, duration, privacyStatus,
                duration <= TimeSpan.FromSeconds(60),
                video.Snippet?.PublishedAtDateTimeOffset ?? DateTimeOffset.MinValue,
                video.Snippet?.CategoryId, video.Snippet?.DefaultLanguage, video.Snippet?.DefaultAudioLanguage,
                location, video.RecordingDetails?.LocationDescription,
                video.ETag, null // CommentsAllowed determined during comment scanning
            ));
        }

        return result;
    }

    public async Task ReplyAsync(string parentCommentId, string text,
        CancellationToken cancellationToken)
    {
        var youTubeService = CreateReadOnlyServiceAsync(await GetCurrentUsersAccessToken(cancellationToken));
        var comment = new Comment { Snippet = new CommentSnippet { ParentId = parentCommentId, TextOriginal = text } };

        await youTubeService.Comments.Insert(comment, "snippet").ExecuteAsync(cancellationToken);
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
        var youTubeService = CreateReadOnlyServiceAsync(await GetCurrentUsersAccessToken(cancellationToken));

        var snippet = new VideoSnippet
        {
            Title = title,
            Description = description,
            Tags = tags.ToList(),
            CategoryId = categoryId,
            DefaultLanguage = defaultLanguage,
            DefaultAudioLanguage = defaultAudioLanguage
        };

        var video = new Video { Id = videoId, Snippet = snippet };

        var up = youTubeService.Videos.Update(video, "snippet");
        await up.ExecuteAsync(cancellationToken);
    }

    public async Task AddVideoToPlaylistAsync(
        string playlistId,
        string videoId,
        CancellationToken cancellationToken)
    {
        var youTubeService = CreateReadOnlyServiceAsync(await GetCurrentUsersAccessToken(cancellationToken));

        //todo avoid checking to save the calls?
        // Check if already present to avoid duplicates
        string? page = null;
        do
        {
            var list = youTubeService.PlaylistItems.List("contentDetails");
            list.PlaylistId = playlistId;
            list.MaxResults = 50;
            list.PageToken = page;
            var res = await list.ExecuteAsync(cancellationToken);

            if (res.Items.Any(i => i.ContentDetails?.VideoId == videoId))
            {
                return; // already there
            }

            page = res.NextPageToken;
        } while (page is not null);

        var insert = youTubeService.PlaylistItems.Insert(
            new PlaylistItem
            {
                Snippet = new PlaylistItemSnippet
                {
                    PlaylistId = playlistId,
                    ResourceId = new ResourceId { Kind = "youtube#video", VideoId = videoId }
                }
            }, "snippet");

        await insert.ExecuteAsync(cancellationToken);
    }

    public async IAsyncEnumerable<PlaylistDto> GetPlaylistsAsync(
        string channelId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var youTubeService = CreateReadOnlyServiceAsync(await GetCurrentUsersAccessToken(cancellationToken));

        string? page = null;
        do
        {
            var playlistRequest = youTubeService.Playlists.List("id,snippet");
            playlistRequest.ChannelId = channelId;
            playlistRequest.MaxResults = 50;
            playlistRequest.PageToken = page;

            var playlistResponse = await playlistRequest.ExecuteAsync(cancellationToken);

            if (playlistResponse.Items is null || playlistResponse.Items.Count == 0)
            {
                yield break;
            }

            foreach (var playlist in playlistResponse.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(playlist.Id))
                {
                    yield return new PlaylistDto(playlist.Id!, playlist.Snippet.Title, playlist.Snippet.ETag);
                }
            }

            page = playlistResponse.NextPageToken;
        } while (!string.IsNullOrEmpty(page));
    }

    public async IAsyncEnumerable<string> GetPlaylistVideoIdsAsync(
        string playlistId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var youTubeService = CreateReadOnlyServiceAsync(await GetCurrentUsersAccessToken(cancellationToken));

        string? page = null;
        do
        {
            var itemsRequest = youTubeService.PlaylistItems.List("contentDetails");
            itemsRequest.PlaylistId = playlistId;
            itemsRequest.MaxResults = 50;
            itemsRequest.PageToken = page;

            var itemsResponse = await itemsRequest.ExecuteAsync(cancellationToken);

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

    private async Task<string> GetCurrentUsersAccessToken(CancellationToken cancellationToken)
    {
        var accessToken = await currentUserTokenAccessor.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("No access token is available for the current user.");
        }

        return accessToken;
    }

    private static YouTubeService CreateReadOnlyServiceAsync(string accessToken)
    {
        var googleCredential = GoogleCredential
            .FromAccessToken(accessToken)
            .CreateScoped(YouTubeService.Scope.YoutubeReadonly);

        var initializer = new BaseClientService.Initializer
        {
            HttpClientInitializer = googleCredential,
            ApplicationName = "Tubester"
        };

        return new YouTubeService(initializer);
    }
}