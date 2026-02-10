namespace Tubester.Abstractions.Auth;

/// <summary>
/// Provides access to persisted authentication tokens for a user.
/// </summary>
public interface IUserTokenStore
{
    /// <summary>
    /// Gets the stored token data for the specified user.
    /// </summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The stored token data, or null if none exists.</returns>
    Task<UserTokenData?> GetAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or updates the stored token data for the specified user.
    /// </summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="accessToken">Access token to persist.</param>
    /// <param name="refreshToken">Refresh token to persist.</param>
    /// <param name="expiresAt">Access token expiry moment, in coordinated universal time.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task UpsertAsync(
        string userId,
        string? accessToken,
        string? refreshToken,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken);
}
