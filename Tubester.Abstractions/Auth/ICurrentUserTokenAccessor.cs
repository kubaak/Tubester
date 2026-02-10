namespace Tubester.Abstractions.Auth;

/// <summary>
/// Provides access to the current authenticated user's Google/YouTube tokens
/// stored in the authentication ticket (cookie) and creates read-only
/// YouTubeService instances on demand.
/// </summary>
public interface ICurrentUserTokenAccessor
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}