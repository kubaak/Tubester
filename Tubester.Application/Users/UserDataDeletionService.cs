using Microsoft.Extensions.Logging;
using Tubester.Abstractions;
using Tubester.Abstractions.Account;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Users;
using Tubester.Application.Common;

namespace Tubester.Application.Users;

/// <summary>
/// Service for deleting user data and all Google/YouTube-derived content.
/// Used to support Google OAuth app review requirements for account deletion.
/// </summary>
public sealed class UserDataDeletionService(
    IUserRepository userRepository,
    IChannelRepository channelRepository,
    IAccountSettingsRepository accountSettingsRepository,
    ICreditsStore creditsStore,
    IUserEventLogger userEventLogger,
    IDateTimeOffsetProvider dateTimeOffsetProvider,
    ILogger<UserDataDeletionService> logger) : IUserDataDeletionService
{
    /// <inheritdoc />
    public async Task DeleteUserDataAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            [LoggingConstants.UserId] = userId
        });

        logger.LogInformation("Starting user data deletion for user {UserId}", userId);

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        try
        {
            // Step 1: Cancel subscription (preserves history for billing/accounting)
            await creditsStore.CancelSubscriptionAsync(userId, nowUtc, cancellationToken);
            logger.LogDebug("Subscription cancelled for user {UserId}", userId);

            // Step 2: Close wallet (zero out balance, mark as expired)
            await creditsStore.CloseWalletAsync(userId, nowUtc, cancellationToken);
            logger.LogDebug("Wallet closed for user {UserId}", userId);

            // Step 3: Delete user events (preserves only the deletion audit event)
            await userEventLogger.DeleteByUserIdAsync(
                userId,
                UserEventType.UserDataDeletionRequested,
                nowUtc,
                cancellationToken);
            logger.LogDebug("User events deleted for user {UserId}", userId);

            // Step 4: Delete account settings
            await accountSettingsRepository.DeleteByUserIdAsync(userId, cancellationToken);
            logger.LogDebug("Account settings deleted for user {UserId}", userId);

            // Step 5: Delete channels (cascades to: ChannelSettings, Videos, Replies, VideoPlaylists)
            // Channel -> Videos (via UploadsPlaylistId FK)
            // Channel -> ChannelSettings (via ChannelId FK with cascade)
            // Video -> Replies (via VideoId FK with cascade)
            // Video -> VideoPlaylists (via VideoId FK with cascade)
            var channelDeletedCount = await channelRepository.DeleteByUserIdAsync(userId, cancellationToken);
            logger.LogDebug("Deleted {ChannelCount} channels for user {UserId}", channelDeletedCount, userId);

            // Step 6: Mark user as deleted (clears email, name, picture, sets IsDeleted=true)
            await userRepository.MarkAsDeletedAsync(userId, nowUtc, cancellationToken);
            logger.LogInformation("User data deletion completed for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to complete user data deletion for user {UserId}", userId);
            throw;
        }
    }
}