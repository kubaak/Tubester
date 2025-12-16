using System.Runtime.CompilerServices;
using YouTubester.Integration.Dtos;

namespace YouTubester.Integration;

public interface IBackgroundYoutubeIntegration
{
    IAsyncEnumerable<CommentThreadDto> GetUnansweredTopLevelCommentsAsync(
        string channelId,
        string videoId,
        [EnumeratorCancellation] CancellationToken cancellationToken);
}