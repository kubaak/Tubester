namespace Tubester.Abstractions.Credits;

public interface ICreditsStore
{
    Task<CreditActionCostDto?> GetActionCostAsync(string actionType, CancellationToken cancellationToken);

    Task<WalletDto?> GetWalletAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetUserIdsWithExpiredWalletsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<SubscriptionDto?> GetActiveSubscriptionAsync(
        string userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<SpendResult> TrySpendAsync(
        string userId,
        string actionType,
        int cost,
        string idempotencyKey,
        string? referenceId,
        string? metadataJson,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken);

    Task<GrantResult> GrantPeriodCreditsAsync(
        string userId,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        int periodCredits,
        string idempotencyKey,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Refunds a previously successful spend identified by <paramref name="originalIdempotencyKey"/>.
    /// A compensating ledger entry with a positive delta is written and the wallet balance is increased.
    /// The operation is idempotent per <paramref name="refundIdempotencyKey"/>.
    /// </summary>
    Task RefundAsync(
        string userId,
        string actionType,
        string originalIdempotencyKey,
        string refundIdempotencyKey,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the subscription for the specified user regardless of status or period.
    /// Returns null if the user has no subscription.
    /// </summary>
    Task<UserSubscriptionDto?> GetUserSubscriptionAsync(
        string userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Assigns the free plan subscription to the specified user, creates a wallet, and grants initial credits.
    /// </summary>
    Task AssignFreeSubscriptionAsync(
        string userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a subscription summary for the specified user regardless of status or period.
    /// Returns null if the user has no subscription.
    /// </summary>
    Task<SubscriptionSummaryDto?> GetSubscriptionSummaryAsync(
        string userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Grants credits to a user's wallet directly (admin operation).
    /// This adds credits without resetting the wallet balance (unlike period grants).
    /// </summary>
    Task<GrantResult> AdminGrantCreditsAsync(
        string userId,
        int amount,
        string idempotencyKey,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken);

    Task<SubscriptionDto?> TryRenewSubscriptionAsync(
        string userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
