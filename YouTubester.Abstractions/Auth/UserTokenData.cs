namespace YouTubester.Abstractions.Auth;

/// <summary>
/// Represents authentication tokens for a specific user.
/// </summary>
public sealed class UserTokenData
{
    /// <summary>
    /// Gets the identifier of the user that owns the tokens.
    /// </summary>
    public string UserId { get; init; } = default!;

    /// <summary>
    /// Gets the access token issued for the user.
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>
    /// Gets the refresh token that can be used to obtain new access tokens.
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Gets the moment when the access token expires, in coordinated universal time.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
