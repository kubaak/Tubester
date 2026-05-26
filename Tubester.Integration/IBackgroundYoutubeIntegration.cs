using Tubester.Integration.Dtos;

namespace Tubester.Integration;

public interface IBackgroundYoutubeIntegration
{
    IAsyncEnumerable<CommentThreadDto> GetUnansweredTopLevelCommentsAsync(
        string channelId,
        string videoId,
        CancellationToken cancellationToken);
}
