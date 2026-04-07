namespace Tubester.Abstractions.Credits;

public sealed class SubscriptionSummaryDto
{
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset PeriodStartUtc { get; set; }
    public DateTimeOffset PeriodEndUtc { get; set; }
    public int MonthlyCredits { get; set; }
}
