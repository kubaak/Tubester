using Tubester.Abstractions.Credits;

namespace Tubester.Application.Credits;

public interface ICreditsService
{
    Task<SpendResult> TrySpendAsync(
        string userId,
        string actionType,
        string idempotencyKey,
        string? referenceId,
        object? metadata,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to spend credits for multiple actions atomically.
    /// All actions must have sufficient credits or the entire batch fails (all-or-nothing).
    /// Duplicate detection is per-action based on idempotency keys.
    /// </summary>
    Task<SpendResult> TrySpendBatchAsync(
        BatchSpendRequest request,
        CancellationToken cancellationToken);

    Task RefundAsync(
        string userId,
        string actionType,
        string originalIdempotencyKey,
        string refundIdempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Grants monthly credits to all users whose wallet period has expired.
    /// Returns the number of wallets that were updated.
    /// </summary>
    Task<int> GrantPeriodCreditsIfDueAsync(CancellationToken cancellationToken);

    Task<GrantResult> GrantAdminCreditsAsync(
        string adminUserId,
        string targetUserId,
        int amount,
        string operationId,
        CancellationToken ct);
}