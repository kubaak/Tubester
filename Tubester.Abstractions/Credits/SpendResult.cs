namespace Tubester.Abstractions.Credits;

public sealed class SpendResult
{
    public bool Succeeded { get; set; }

    public int? NewBalance { get; set; }

    public bool WasDuplicate { get; set; }
    public SpendFailureReason FailureReason { get; init; } = SpendFailureReason.None;
}