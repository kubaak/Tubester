namespace YouTubester.Persistence.Credits;

public sealed class Wallet
{
    public string UserId { get; set; } = string.Empty;

    public int Balance { get; set; }

    public DateTimeOffset PeriodStartUtc { get; set; }

    public DateTimeOffset PeriodEndUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}