namespace YouTubester.Abstractions.Credits;

public sealed class WalletDto
{
    public string UserId { get; set; } = string.Empty;

    public int Balance { get; set; }

    public DateTimeOffset PeriodStartUtc { get; set; }

    public DateTimeOffset PeriodEndUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}