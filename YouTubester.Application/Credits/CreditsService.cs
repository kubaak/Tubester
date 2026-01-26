using System.Text.Json;
using Microsoft.Extensions.Logging;
using YouTubester.Abstractions.Credits;

namespace YouTubester.Application.Credits;

public sealed class CreditsService(
    ICreditsStore creditsStore,
    ILogger<CreditsService> logger,
    IDateTimeOffsetProvider dateTimeOffsetProvider)
    : ICreditsService
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<bool> TrySpendAsync(
        string userId,
        string actionType,
        string idempotencyKey,
        string? referenceId,
        object? metadata,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new ArgumentException("Action type is required.", nameof(actionType));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        var actionCost = await creditsStore.GetActionCostAsync(actionType, cancellationToken);

        if (actionCost is null || !actionCost.IsEnabled)
        {
            logger.LogError(
                "Attempted to spend credits for action type {ActionType} without an enabled cost configuration",
                actionType);
            throw new InvalidOperationException(
                $"No enabled credit cost configured for action type '{actionType}'");
        }

        var cost = actionCost.Cost;
        var metadataJson = SerializeMetadata(metadata);

        var plan = await creditsStore.GetActiveUserPlanAsync(userId, nowUtc, cancellationToken);
        if (plan is null)
        {
            logger.LogWarning("User {UserId} has no active subscription; cannot spend credits", userId);
            return false;
        }

        var wallet = await creditsStore.GetWalletAsync(userId, cancellationToken);

        var walletNeedsRefresh = wallet is null
                                 || wallet.PeriodEndUtc.UtcDateTime <= nowUtc.UtcDateTime
                                 || wallet.PeriodStartUtc.UtcDateTime != plan.PeriodStartUtc.UtcDateTime;

        if (walletNeedsRefresh)
        {
            var grantIdempotencyKey = BuildGrantIdempotencyKey(userId, plan.PeriodStartUtc);

            await creditsStore.GrantPeriodCreditsAsync(
                userId,
                plan.PeriodStartUtc,
                plan.PeriodEndUtc,
                plan.PeriodCredits,
                grantIdempotencyKey,
                nowUtc,
                cancellationToken);

            wallet = await creditsStore.GetWalletAsync(userId, cancellationToken);
            if (wallet is null)
            {
                logger.LogError("Wallet not found for user {UserId} after granting period credits", userId);
                return false;
            }
        }

        var spendResult = await creditsStore.TrySpendAsync(
            userId,
            actionType,
            cost,
            idempotencyKey,
            referenceId,
            metadataJson,
            nowUtc,
            cancellationToken);

        return spendResult.Succeeded;
    }

    public async Task RefundAsync(
        string userId,
        string actionType,
        string originalIdempotencyKey,
        string refundIdempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new ArgumentException("Action type is required.", nameof(actionType));
        }

        if (string.IsNullOrWhiteSpace(originalIdempotencyKey))
        {
            throw new ArgumentException("Original idempotency key is required.", nameof(originalIdempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(refundIdempotencyKey))
        {
            throw new ArgumentException("Refund idempotency key is required.", nameof(refundIdempotencyKey));
        }

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        await creditsStore.RefundAsync(
            userId,
            actionType,
            originalIdempotencyKey,
            refundIdempotencyKey,
            nowUtc,
            cancellationToken);
    }

    public async Task<int> GrantPeriodCreditsIfDueAsync(CancellationToken cancellationToken)
    {
        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        var userIdsWithExpiredWallets = await creditsStore.GetUserIdsWithExpiredWalletsAsync(nowUtc, cancellationToken);

        if (userIdsWithExpiredWallets.Count == 0)
        {
            return 0;
        }

        var updatedWalletCount = 0;

        foreach (var userId in userIdsWithExpiredWallets)
        {
            var activePlan = await creditsStore.GetActiveUserPlanAsync(userId, nowUtc, cancellationToken);
            if (activePlan is null)
            {
                logger.LogWarning(
                    "Skipping credit grant for user {UserId} because there is no active subscription or plan is inactive",
                    userId);
                continue;
            }

            var grantIdempotencyKey = BuildGrantIdempotencyKey(userId, activePlan.PeriodStartUtc);

            var grantResult = await creditsStore.GrantPeriodCreditsAsync(
                userId,
                activePlan.PeriodStartUtc,
                activePlan.PeriodEndUtc,
                activePlan.PeriodCredits,
                grantIdempotencyKey,
                nowUtc,
                cancellationToken);

            if (grantResult.Granted)
            {
                updatedWalletCount++;
            }
        }

        return updatedWalletCount;
    }

    private static string BuildGrantIdempotencyKey(string userId, DateTimeOffset periodStartUtc)
    {
        var utc = periodStartUtc.ToUniversalTime();
        return $"grant:{userId}:{utc:O}";
    }

    private static string? SerializeMetadata(object? metadata)
    {
        return metadata is null ? null : JsonSerializer.Serialize(metadata, _jsonSerializerOptions);
    }
}