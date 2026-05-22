namespace Tubester.Abstractions.Credits;

/// <summary>
/// Represents a single spend action within a batch request.
/// </summary>
/// <param name="ActionType">The type of action to bill.</param>
/// <param name="IdempotencyKey">Unique key for idempotency of this specific action.</param>
/// <param name="ReferenceId">Optional reference ID (e.g., video ID).</param>
/// <param name="Metadata">Optional metadata to store with the ledger entry.</param>
public sealed class BatchSpendAction(
    string actionType,
    string idempotencyKey,
    string? referenceId,
    object? metadata)
{
    public string ActionType { get; } = actionType;
    public string IdempotencyKey { get; } = idempotencyKey;
    public string? ReferenceId { get; } = referenceId;
    public object? Metadata { get; } = metadata;
}

/// <summary>
/// Request to spend credits for multiple actions atomically.
/// All actions must succeed or the entire batch fails.
/// </summary>
/// <param name="UserId">The user spending credits.</param>
/// <param name="Actions">The collection of actions to bill.</param>
public sealed class BatchSpendRequest
{
    public required string UserId { get; init; }

    public required IReadOnlyList<BatchSpendAction> Actions { get; init; }
}