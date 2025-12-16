using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubester.Abstractions.Auth;
using YouTubester.Integration.Configuration;
using YouTubester.Integration.Exceptions;

namespace YouTubester.Integration;

public sealed class YouTubeServiceFactory(
    IOptions<GoogleAuthOptions> options,
    IUserTokenStore userTokenStore,
    ILogger<YouTubeServiceFactory> logger) : IYouTubeServiceFactory
{
    public async Task<YouTubeService> CreateAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id must be a non-empty string.", nameof(userId));
        }

        var authOptions = options.Value;

        var userTokens = await userTokenStore.GetAsync(userId, cancellationToken);
        if (userTokens is null)
        {
            logger.LogWarning(
                "Cannot create YouTubeService for user {UserId} because no Google tokens were found",
                userId);

            throw new UserAuthorizationRequiredException(userId);
        }

        var now = DateTimeOffset.UtcNow;
        var hasAccessToken = !string.IsNullOrWhiteSpace(userTokens.AccessToken);
        var hasRefreshToken = !string.IsNullOrWhiteSpace(userTokens.RefreshToken);
        var isAccessTokenValid =
            hasAccessToken &&
            userTokens.ExpiresAt is { } expiresAt &&
            expiresAt > now;

        // Prefer refresh-token-based credential (auto refresh, good for background jobs)
        if (hasRefreshToken)
        {
            var tokenResponse = BuildTokenResponse(userTokens);

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = authOptions.ClientId,
                    ClientSecret = authOptions.ClientSecret
                },
                Scopes = [YouTubeService.Scope.YoutubeForceSsl]
            });

            var credential = new UserCredential(flow, userId, tokenResponse);

            return new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = authOptions.ApplicationName
            });
        }

        // No refresh token, but access token is still valid -> use access token only
        if (isAccessTokenValid)
        {
            logger.LogInformation(
                "Creating YouTubeService for user {UserId} with access token only (no refresh token available)",
                userId);

            var googleCredential = GoogleCredential
                .FromAccessToken(userTokens.AccessToken!)
                .CreateScoped(YouTubeService.Scope.YoutubeForceSsl);

            return new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = googleCredential,
                ApplicationName = authOptions.ApplicationName
            });
        }

        // No refresh token AND access token is expired/unknown -> we can't recover server-side
        logger.LogWarning(
            "Cannot create YouTubeService for user {UserId} because tokens are expired and no refresh token is available",
            userId);

        throw new UserAuthorizationRequiredException(
            userId,
            $"Google tokens for user '{userId}' have expired and cannot be refreshed. The user must reconnect their Google account.");
    }

    private static TokenResponse BuildTokenResponse(UserTokenData userToken)
    {
        long? expiresInSeconds = null;

        if (userToken.ExpiresAt.HasValue)
        {
            var now = DateTimeOffset.UtcNow;
            var remaining = userToken.ExpiresAt.Value - now;
            if (remaining > TimeSpan.Zero)
            {
                expiresInSeconds = (long)remaining.TotalSeconds;
            }
        }

        return new TokenResponse
        {
            AccessToken = userToken.AccessToken,
            RefreshToken = userToken.RefreshToken,
            ExpiresInSeconds = expiresInSeconds
        };
    }
}