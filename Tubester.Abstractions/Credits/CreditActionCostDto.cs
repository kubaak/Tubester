namespace Tubester.Abstractions.Credits;

public sealed class CreditActionCostDto
{
    public string ActionType { get; set; } = string.Empty;

    public int Cost { get; set; }
}
