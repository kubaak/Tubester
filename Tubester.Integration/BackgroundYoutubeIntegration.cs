using System.Net;
using System.Runtime.CompilerServices;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Integration.Configuration;
using Tubester.Integration.Dtos;
using Tubester.Integration.Exceptions;

namespace Tubester.Integration;

public class BackgroundYoutubeIntegration() : IBackgroundYoutubeIntegration
{
    private readonly YouTubeService _youTubeService;
    private readonly ILogger<BackgroundYoutubeIntegration> _logger;

    public BackgroundYoutubeIntegration(IOptions<YouTubeApiOptions> apiOptions,
        ILogger<BackgroundYoutubeIntegration> logger) : this()
    {
        _logger = logger;
        var apiKey = apiOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "YouTube API key is not configured. Please set YouTubeApi:ApiKey in configuration.");
        }

        _youTubeService = new YouTubeService(new BaseClientService.Initializer
        {
            ApiKey = apiKey,
            ApplicationName = "Tubester"
        });
    }

    public async IAsyncEnumerable<CommentThreadDto> GetUnansweredTopLevelCommentsAsync(
        string channelId,
        string videoId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? page = null;

        do
        {
            var ctReq = _youTubeService.CommentThreads.List("snippet,replies");
            ctReq.VideoId = videoId;
            ctReq.MaxResults = 50;
            ctReq.PageToken = page;
            ctReq.TextFormat = CommentThreadsResource.ListRequest.TextFormatEnum.PlainText;

            CommentThreadListResponse ctRes;
            try
            {
                ctRes = await ctReq.ExecuteAsync(cancellationToken);
            }
            catch (Google.GoogleApiException ex)
            {
                // 403 Forbidden + specific message → comments disabled
                if (ex.HttpStatusCode == HttpStatusCode.Forbidden &&
                    ex.Message.Contains("has disabled comments", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(ex, "Comments disabled for video {VideoId}", videoId);
                    throw new CommentsDisabledException(
                        videoId,
                        "The video has disabled comments and cannot be scanned.",
                        ex);
                }
                _logger.LogError(ex, "Error from Google API scanning comments for video {VideoId}", videoId);

                // Anything else bubble up
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning comments for video {VideoId}", videoId);
                throw;
            }

            foreach (var t in ctRes.Items)
            {
                var top = t.Snippet?.TopLevelComment;
                if (top is null)
                {
                    continue;
                }

                var author = top.Snippet?.AuthorChannelId?.Value ?? string.Empty;

                // Skip our own comments (owner replies)
                if (!string.IsNullOrEmpty(author) && author == channelId)
                {
                    continue;
                }

                // Already answered by us?
                var anyOwnerReply = (t.Replies?.Comments ?? new List<Comment>())
                    .Any(r => r.Snippet?.AuthorChannelId?.Value == channelId);

                if (anyOwnerReply)
                {
                    continue;
                }

                yield return new CommentThreadDto(
                    top.Id!,
                    videoId,
                    author,
                    top.Snippet?.TextDisplay ?? string.Empty,
                    top.Snippet?.PublishedAtDateTimeOffset
                );
            }

            page = ctRes.NextPageToken;
        } while (page != null);
    }
}