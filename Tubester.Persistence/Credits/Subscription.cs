namespace Tubester.Persistence.Credits;

public sealed class Subscription
{
    public string UserId { get; set; } = string.Empty;

    public int PlanId { get; set; }

    public DateTimeOffset PeriodStartUtc { get; set; }

    public DateTimeOffset PeriodEndUtc { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Unknown;

    public Plan Plan { get; set; } = null!;
}
