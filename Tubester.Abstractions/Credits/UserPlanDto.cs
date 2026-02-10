namespace Tubester.Abstractions.Credits;

public sealed class UserPlanDto
{
    public string UserId { get; set; } = string.Empty;

    public int PlanId { get; set; }

    public string PlanCode { get; set; } = string.Empty;

    public int PeriodCredits { get; set; }

    public DateTimeOffset PeriodStartUtc { get; set; }

    public DateTimeOffset PeriodEndUtc { get; set; }
}