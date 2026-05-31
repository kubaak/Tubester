namespace Tubester.Abstractions.Auth;

/// <summary>
/// Context containing user information available after successful authentication.
/// </summary>
public sealed record SuccessfulLoginContext(
    string UserId,
    string? Email,
    string? Name,
    string? Picture,
    string? ChannelId,
    DateTimeOffset LoginAt);

/// <summary>
/// Handles user onboarding tasks after successful authentication.
/// </summary>
public interface IUserOnboardingService
{
    /// <summary>
    /// Processes onboarding for a user after successful login.
    /// This includes:
    /// - Ensuring the user has the default/free plan or wallet if needed
    /// - Ensuring channel settings exist
    /// - Queueing initial comment scan once (if user is new)
    /// </summary>
    Task HandleSuccessfulLoginAsync(
        SuccessfulLoginContext context,
        CancellationToken cancellationToken);
}
