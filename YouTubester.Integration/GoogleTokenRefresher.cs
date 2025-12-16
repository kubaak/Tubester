using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubester.Abstractions.Auth;
using YouTubester.Integration.Configuration;
using YouTubester.Integration.Exceptions;

namespace YouTubester.Integration;

public sealed class GoogleTokenRefresher(
    HttpClient httpClient,
    IOptions<GoogleAuthOptions> youTubeAuthOptions,
    ILogger<GoogleTokenRefresher> logger) : IGoogleTokenRefresher
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public async Task<UserTokenData> RefreshAccessTokenAsync(
        UserTokenData userTokenData,
        CancellationToken cancellationToken)
    {
        if (userTokenData is null)
        {
            throw new ArgumentNullException(nameof(userTokenData));
        }

        if (string.IsNullOrWhiteSpace(userTokenData.RefreshToken))
        {
            throw new InvalidOperationException(
                "Cannot refresh Google access token because no refresh token is available.");
        }

        var options = youTubeAuthOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                "YouTubeAuthOptions are not configured correctly. ClientId and ClientSecret are required to refresh tokens.");
        }

        var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["refresh_token"] = userTokenData.RefreshToken!,
            ["grant_type"] = "refresh_token"
        });

        using var httpRequestMessage =
            new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
        httpRequestMessage.Content = requestContent;

        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        var responseContent = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Failed to refresh Google access token for user {UserId}. StatusCode: {StatusCode}. Body: {Body}",
                userTokenData.UserId,
                (int)httpResponseMessage.StatusCode,
                Truncate(responseContent, 512));

            throw new UserAuthorizationRequiredException(
                userTokenData.UserId,
                $"Failed to refresh Google access token for user '{userTokenData.UserId}'. The user must reconnect their Google account.");
        }

        GoogleTokenResponse? googleTokenResponse;
        try
        {
            googleTokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(responseContent);
        }
        catch (JsonException jsonException)
        {
            logger.LogError(jsonException,
                "Failed to deserialize Google token refresh response for user {UserId}. Body: {Body}",
                userTokenData.UserId,
                Truncate(responseContent, 512));

            throw new InvalidOperationException(
                "Failed to parse Google token refresh response.",
                jsonException);
        }

        if (googleTokenResponse is null || string.IsNullOrWhiteSpace(googleTokenResponse.AccessToken))
        {
            logger.LogError(
                "Google token refresh response for user {UserId} did not contain an access token. Body: {Body}",
                userTokenData.UserId,
                Truncate(responseContent, 512));

            throw new InvalidOperationException(
                "Google token refresh response did not contain an access token.");
        }

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt = null;
        if (googleTokenResponse.ExpiresInSeconds.HasValue && googleTokenResponse.ExpiresInSeconds.Value > 0)
        {
            expiresAt = now.AddSeconds(googleTokenResponse.ExpiresInSeconds.Value);
        }

        var refreshToken = string.IsNullOrWhiteSpace(googleTokenResponse.RefreshToken)
            ? userTokenData.RefreshToken
            : googleTokenResponse.RefreshToken;

        var refreshedUserTokenData = new UserTokenData
        {
            UserId = userTokenData.UserId,
            AccessToken = googleTokenResponse.AccessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt
        };

        return refreshedUserTokenData;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength);
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")] public int? ExpiresInSeconds { get; set; }

        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    }
}