using Google.Apis.YouTube.v3;

namespace Tubester.Integration;

public interface IYouTubeServiceFactory
{
    Task<YouTubeService> CreateAsync(string userId, CancellationToken cancellationToken);
}