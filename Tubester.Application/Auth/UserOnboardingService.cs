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

        var user = await userRepository.UpsertUserAsync(
            context.UserId,
            context.Email,
            context.Name,
            context.Picture,
            context.LoginAt,
            cancellationToken);

        var isNewUser = user.IsNew;
        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

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