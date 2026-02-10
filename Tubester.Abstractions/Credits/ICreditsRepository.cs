namespace Tubester.Abstractions.Credits;

/// <summary>
/// Provides data access operations related to user credits and billing plans.
/// </summary>
public interface ICreditsRepository
{
    /// <summary>
    /// Gets the wallet balance for the specified user.
    /// </summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The current wallet balance.</returns>
    Task<int> GetWalletBalanceAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether there is already a ledger entry with the specified idempotency key.
    /// </summary>
    /// <param name="idempotencyKey">Idempotency key that identifies the ledger entry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True if a ledger entry exists, otherwise false.</returns>
    Task<bool> HasExistingLedgerEntryAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the cost and enabled status for a specified action type.
    /// </summary>
    /// <param name="actionType">Identifier of the action type.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The cost and whether the action type is currently enabled.</returns>
    Task<(int Cost, bool IsEnabled)> GetActionCostAsync(string actionType, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to spend credits from the specified user's wallet.
    /// </summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="actionType">Identifier of the action type that is being charged.</param>
    /// <param name="idempotencyKey">Idempotency key that uniquely identifies the spend operation.</param>
    /// <param name="referenceId">Optional reference identifier related to the operation.</param>
    /// <param name="metadataJson">Optional metadata stored as JavaScript object notation.</param>
    /// <param name="nowUtc">Current moment in coordinated universal time.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True if the credits were successfully spent, otherwise false.</returns>
    Task<bool> TrySpendAsync(
        string userId,
        string actionType,
        string idempotencyKey,
        string? referenceId,
        string? metadataJson,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the start and end bounds of the current billing period.
    /// </summary>
    /// <param name="nowUtc">Current moment in coordinated universal time.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The period start and end bounds in coordinated universal time.</returns>
    Task<(DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndUtc)> GetCurrentPeriodBoundsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a list of user identifiers whose wallets have expired.
    /// </summary>
    /// <param name="nowUtc">Current moment in coordinated universal time.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>List of user identifiers.</returns>
    Task<IReadOnlyList<string>> GetUserIdsWithExpiredWalletsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets active subscriptions for the specified users along with their plans.
    /// </summary>
    /// <param name="userIds">User identifiers to query.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A list of user and plan information.</returns>
    Task<IReadOnlyList<(string UserId, int PlanId, string PlanCode, int MonthlyCredits)>>
        GetActiveSubscriptionsWithPlansAsync(
            IReadOnlyList<string> userIds,
            CancellationToken cancellationToken);

    /// <summary>
    /// Grants monthly credits for the provided subscriptions and records the operation.
    /// </summary>
    /// <param name="subscriptions">Subscriptions for which to grant credits.</param>
    /// <param name="periodStartUtc">Start of the period the credits apply to, in coordinated universal time.</param>
    /// <param name="periodEndUtc">End of the period the credits apply to, in coordinated universal time.</param>
    /// <param name="nowUtc">Current moment in coordinated universal time.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The total number of credits granted.</returns>
    Task<int> GrantMonthlyCreditsAsync(
        IReadOnlyList<(string UserId, int PlanId, string PlanCode, int MonthlyCredits)> subscriptions,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
