namespace Tubester.Integration.Exceptions;

/// <summary>
/// Thrown when YouTube operations cannot proceed because the user's
/// Google authorization is missing or no longer usable (e.g. tokens
/// are missing or expired and cannot be refreshed).
/// </summary>
public sealed class UserAuthorizationRequiredException : InvalidOperationException
{
    public string UserId { get; }

    public UserAuthorizationRequiredException(string userId)
        : base($"Google authorization is required for user '{userId}'. The user must reconnect their Google account.")
    {
        UserId = userId;
    }

    public UserAuthorizationRequiredException(string userId, string message)
        : base(message)
    {
        UserId = userId;
    }
}