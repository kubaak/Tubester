namespace Tubester.Abstractions.Credits;

public sealed class SpendResult
{
    public bool Succeeded { get; set; }

    public int? NewBalance { get; set; }

    public bool WasDuplicate { get; set; }

    public SpendFailureReason FailureReason { get; init; } = SpendFailureReason.None;

    /// <summary>
    /// The actual cost that was spent or attempted to be spent.
    /// On success, this is the cost that was deducted.
    /// On failure, this is the cost that would have been spent.
    /// </summary>
    public int? FinalCost { get; set; }
}
