namespace Tubester.Application.Credits;

public interface ICreditsService
{
    Task<bool> TrySpendAsync(
        string userId,
        string actionType,
        string idempotencyKey,
        string? referenceId,
        object? metadata,
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
}