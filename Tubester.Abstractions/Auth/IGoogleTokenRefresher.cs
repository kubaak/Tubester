namespace Tubester.Abstractions.Auth;

public interface IGoogleTokenRefresher
{
    /// <summary>
    /// Uses the refresh token in <paramref name="userTokenData"/> to obtain a new access token
    /// and expiry (and potentially a new refresh token).
    /// </summary>
    Task<UserTokenData> RefreshAccessTokenAsync(
        UserTokenData userTokenData,
        CancellationToken cancellationToken);
}