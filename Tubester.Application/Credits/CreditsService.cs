using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using Tubester.Abstractions;
using Tubester.Abstractions.Credits;
using Tubester.Application.Common;

namespace Tubester.Application.Credits;

public sealed class CreditsService(
    ICreditsStore creditsStore,
    ILogger<CreditsService> logger,
    IDateTimeOffsetProvider dateTimeOffsetProvider)
    : ICreditsService
{
    public async Task<SpendResult> TrySpendAsync(
        string userId,
        string actionType,
        string idempotencyKey,
        string? referenceId,
        object? metadata,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        var actionCost = await GetEnabledActionCostAsync(actionType, cancellationToken);

        var subscription = await TryEnsureActiveSubscriptionAsync(
            userId,
            nowUtc,
            cancellationToken);

        if (subscription is null)
        {
            logger.LogWarning(
                "User {UserId} has no active or renewable subscription; cannot spend credits",
                userId);

            return new SpendResult { NewBalance = 0, Succeeded = false, WasDuplicate = false, FailureReason = SpendFailureReason.NoWallet };
        }

        var walletReady = await EnsureWalletForSubscriptionPeriodAsync(
            userId,
            subscription,
            nowUtc,
            cancellationToken);

        if (!walletReady)
        {
            return new SpendResult { NewBalance = 0, Succeeded = false, WasDuplicate = false, FailureReason = SpendFailureReason.NoWallet };
        }

        var spendResult = await creditsStore.TrySpendAsync(
            userId,
            actionType,
            actionCost.Cost,
            idempotencyKey,
            referenceId,
            SerializeMetadata(metadata),
            nowUtc,
            cancellationToken);

        return spendResult;
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

        logger.LogDebug(
            "Processing refund for user {UserId}, action {ActionType}",
            userId, actionType);

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

        var userIdsWithExpiredWallets = await creditsStore.GetUserIdsWithExpiredWalletsAsync(
            nowUtc,
            cancellationToken);

        if (userIdsWithExpiredWallets.Count == 0)
        {
            return 0;
        }

        logger.LogInformation(
            "Processing period credit grants for {Count} users with expired wallets",
            userIdsWithExpiredWallets.Count);

        var updatedWalletCount = 0;

        foreach (var userId in userIdsWithExpiredWallets)
        {
            var subscription = await TryEnsureActiveSubscriptionAsync(
                userId,
                nowUtc,
                cancellationToken);

            if (subscription is null)
            {
                logger.LogWarning(
                    "Skipping credit grant for user {UserId} because there is no active or renewable subscription",
                    userId);

                continue;
            }

            var grantResult = await GrantSubscriptionPeriodCreditsAsync(
                userId,
                subscription,
                nowUtc,
                cancellationToken);

            if (grantResult.Granted)
            {
                updatedWalletCount++;
            }
        }

        return updatedWalletCount;
    }

    public async Task<GrantResult> GrantAdminCreditsAsync(
        string adminUserId, string targetUserId, int amount, string operationId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var normalizedOperationId = operationId.Trim();

        var idempotencyKey =
            $"admin-grant:{adminUserId}:{targetUserId}:{normalizedOperationId}";

        logger.LogInformation(
            "Admin granting credits: admin {AdminUserId}, target {TargetUserId}, amount {Amount}",
            adminUserId, targetUserId, amount);

        try
        {
            return await creditsStore.AdminGrantCreditsAsync(
                targetUserId,
                amount,
                idempotencyKey,
                dateTimeOffsetProvider.GetUtcNowDateTimeOffset(),
                ct);
        }
        catch (PostgresException e)
            when (e is { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "FK_LedgerEntries_Users_UserId" })
        {
            logger.LogWarning(
                "Admin credit grant failed: target user {TargetUserId} does not exist",
                targetUserId);
            throw new NotFoundException("User does not exist.", e);
        }
    }

    private static string BuildGrantIdempotencyKey(string userId, string planCode, DateTimeOffset periodStartUtc)
    {
        var utc = periodStartUtc.ToUniversalTime();
        return $"period_grant:{userId}:{planCode}:{utc:O}";
    }

    private static string? SerializeMetadata(object? metadata)
    {
        return metadata is null ? null : JsonSerializer.Serialize(metadata, TubesterJsonSerializerOptions.DefaultWrite);
    }

    private async Task<SubscriptionDto?> TryEnsureActiveSubscriptionAsync(
        string userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var subscription = await creditsStore.GetActiveSubscriptionAsync(
            userId,
            nowUtc,
            cancellationToken);

        if (subscription is not null)
        {
            return subscription;
        }

        return await creditsStore.TryRenewSubscriptionAsync(
            userId,
            nowUtc,
            cancellationToken);
    }

    private async Task<CreditActionCostDto> GetEnabledActionCostAsync(
        string actionType,
        CancellationToken cancellationToken)
    {
        var actionCost = await creditsStore.GetActionCostAsync(actionType, cancellationToken);

        if (actionCost is not null && actionCost.IsEnabled)
        {
            return actionCost;
        }

        logger.LogError(
            "Attempted to spend credits for action type {ActionType} without an enabled cost configuration",
            actionType);

        throw new InvalidOperationException(
            $"No enabled credit cost configured for action type '{actionType}'");
    }

    private async Task<bool> EnsureWalletForSubscriptionPeriodAsync(
        string userId,
        SubscriptionDto subscription,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var wallet = await creditsStore.GetWalletAsync(userId, cancellationToken);

        var walletNeedsRefresh = wallet is null
                                 || wallet.PeriodEndUtc.UtcDateTime <= nowUtc.UtcDateTime
                                 || wallet.PeriodStartUtc.UtcDateTime != subscription.PeriodStartUtc.UtcDateTime;

        if (!walletNeedsRefresh)
        {
            return true;
        }

        await GrantSubscriptionPeriodCreditsAsync(
            userId,
            subscription,
            nowUtc,
            cancellationToken);

        var refreshedWallet = await creditsStore.GetWalletAsync(userId, cancellationToken);

        if (refreshedWallet is not null
            && refreshedWallet.PeriodStartUtc.UtcDateTime == subscription.PeriodStartUtc.UtcDateTime
            && refreshedWallet.PeriodEndUtc.UtcDateTime == subscription.PeriodEndUtc.UtcDateTime)
        {
            return true;
        }

        logger.LogError(
            "Wallet for user {UserId} was not refreshed to subscription period {PeriodStartUtc} to {PeriodEndUtc}",
            userId,
            subscription.PeriodStartUtc,
            subscription.PeriodEndUtc);

        return false;
    }

    private async Task<GrantResult> GrantSubscriptionPeriodCreditsAsync(
        string userId,
        SubscriptionDto subscription,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var grantIdempotencyKey = BuildGrantIdempotencyKey(
            userId,
            subscription.PlanCode,
            subscription.PeriodStartUtc);

        return await creditsStore.GrantPeriodCreditsAsync(
            userId,
            subscription.PeriodStartUtc,
            subscription.PeriodEndUtc,
            subscription.PeriodCredits,
            grantIdempotencyKey,
            nowUtc,
            cancellationToken);
    }
}