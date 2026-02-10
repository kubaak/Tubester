namespace Tubester.Persistence.Credits;

public sealed class ActionCost
{
    public string ActionType { get; set; } = string.Empty;

    public int Cost { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? Notes { get; set; }
}