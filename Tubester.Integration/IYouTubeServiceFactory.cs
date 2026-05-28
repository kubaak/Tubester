using Google.Apis.YouTube.v3;

namespace Tubester.Integration;

public interface IYouTubeServiceFactory
{
    /// <summary>
    /// Creates a YouTubeService for the given access token and scope.
    /// This method is used for real-time API calls where the token is already available.
    /// </summary>
    YouTubeService Create(string accessToken, string scope);
}
