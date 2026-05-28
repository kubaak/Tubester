using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Integration.Configuration;

namespace Tubester.Integration;

public sealed class YouTubeServiceFactory(
    IOptions<GoogleAuthOptions> options) : IYouTubeServiceFactory
{
    /// <summary>
    /// Creates a YouTubeService using a pre-obtained access token and specified scope.
    /// This method does not handle token refresh; it assumes the token is valid.
    /// </summary>
    public YouTubeService Create(string accessToken, string scope)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Access token must be a non-empty string.", nameof(accessToken));
        }

        var googleCredential = GoogleCredential
            .FromAccessToken(accessToken)
            .CreateScoped(scope);

        return new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = googleCredential,
            ApplicationName = options.Value.ApplicationName
        });
    }
}