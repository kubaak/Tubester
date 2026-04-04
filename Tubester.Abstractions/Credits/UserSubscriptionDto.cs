namespace Tubester.Abstractions.Credits;

public sealed class UserSubscriptionDto
{
    public string UserId { get; set; } = string.Empty;

    public int PlanId { get; set; }

    public string PlanCode { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
