namespace Tubester.Persistence.Credits;

public sealed class SubscriptionHistory
{
    public string UserId { get; set; } = string.Empty;

    public int PlanId { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Unknown;

    public DateTimeOffset PeriodStartUtc { get; set; }

    public DateTimeOffset PeriodEndUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Plan Plan { get; set; } = null!;
}