using System.Runtime.CompilerServices;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Options;
using YouTubester.Integration.Configuration;
using YouTubester.Integration.Dtos;

namespace YouTubester.Integration;

public class BackgroundYoutubeIntegration : IBackgroundYoutubeIntegration
{
    private readonly YouTubeService _youTubeService;

    public BackgroundYoutubeIntegration(IOptions<YouTubeApiOptions> apiOptions)
    {
        var apiKey = apiOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "YouTube API key is not configured. Please set YouTubeApi:ApiKey in configuration.");
        }

        _youTubeService = new YouTubeService(new BaseClientService.Initializer
        {
            ApiKey = apiKey, ApplicationName = "YouTubester"
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

            var ctRes = await ctReq.ExecuteAsync(cancellationToken);

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
                    top.Snippet?.TextDisplay ?? string.Empty
                );
            }

            page = ctRes.NextPageToken;
        } while (page != null);
    }
}