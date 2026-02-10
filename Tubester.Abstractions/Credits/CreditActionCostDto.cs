namespace Tubester.Abstractions.Credits;

public sealed class CreditActionCostDto
{
    public string ActionType { get; set; } = string.Empty;

    public int Cost { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
