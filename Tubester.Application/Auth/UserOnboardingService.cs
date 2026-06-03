using Hangfire;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions;
using Tubester.Abstractions.Auth;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Users;
using Tubester.Application.Common;
using Tubester.Application.Jobs;

namespace Tubester.Application.Auth;

public sealed class UserOnboardingService(
    IUserRepository userRepository,
    ICreditsStore creditsStore,
    IBackgroundJobClient backgroundJobClient,
    IDateTimeOffsetProvider dateTimeOffsetProvider,
    ILogger<UserOnboardingService> logger) : IUserOnboardingService
{
    public async Task HandleSuccessfulLoginAsync(
        SuccessfulLoginContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.UserId);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            [LoggingConstants.UserId] = context.UserId,
            [LoggingConstants.ChannelId] = context.ChannelId
        });

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        // Check if user exists and was previously deleted
        var existingUser = await userRepository.GetByIdForUpdateAsync(context.UserId, cancellationToken);

        if (existingUser is not null && existingUser.IsDeleted)
        {
            // User was previously deleted - restore from fresh Google login
            // This is NOT treated as a new user; it's a reactivation
            await userRepository.RestoreFromFreshLoginAsync(
                context.UserId,
                context.Email,
                context.Name,
                context.Picture,
                nowUtc,
                cancellationToken);

            logger.LogInformation(
                "Previously deleted user reactivated. UserId: {UserId}",
                context.UserId);

            // Assign fresh subscription for reactivated user (they need to set up again)
            await EnsureFreeSubscriptionAsync(
                context.UserId,
                nowUtc,
                cancellationToken);

            // Reactivated users do NOT trigger initial comment scan automatically
            // They need to go through the onboarding/channel selection flow again
            logger.LogInformation(
                "User reactivation completed. UserId: {UserId}. User must re-authorize and select channel",
                context.UserId);

            return;
        }

        // Normal flow for new or existing non-deleted users
        var user = await userRepository.UpsertUserAsync(
            context.UserId,
            context.Email,
            context.Name,
            context.Picture,
            context.LoginAt,
            cancellationToken);

        var isNewUser = user.IsNew;

        await EnsureFreeSubscriptionAsync(
            context.UserId,
            nowUtc,
            cancellationToken);

        //Channel itself is not persisted yet at this stage
        // if (!string.IsNullOrWhiteSpace(context.ChannelId))
        // {
        //     await channelSettingsService.GetOrCreateAsync(
        //         context.ChannelId,
        //         cancellationToken);
        // }

        var initialScanQueued = false;

        if (isNewUser && !string.IsNullOrWhiteSpace(context.ChannelId))
        {
            initialScanQueued = TryEnqueueInitialCommentScan(context.ChannelId);
            user.MarkAsExisting();
            await userRepository.UpdateUserAsync(user, cancellationToken);
        }

        logger.LogInformation(
            "User onboarding completed. IsNewUser: {IsNewUser}, InitialCommentScanQueued: {InitialScanQueued}",
            isNewUser,
            initialScanQueued);
    }

    private async Task EnsureFreeSubscriptionAsync(
        string userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var existingSubscription = await creditsStore.GetUserSubscriptionAsync(
            userId,
            cancellationToken);

        if (existingSubscription is not null)
        {
            return;
        }

        await creditsStore.AssignFreeSubscriptionAsync(
            userId,
            nowUtc,
            cancellationToken);
    }

    private bool TryEnqueueInitialCommentScan(string channelId)
    {
        try
        {
            backgroundJobClient.Enqueue<CommentScanJob>(
                job => job.Run(
                    channelId,
                    new CommentScanOptions(InitialRun: true),
                    JobCancellationToken.Null));

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enqueue initial comment scan");
            return false;
        }
    }
}