namespace Tubester.Persistence.Credits;

public sealed class LedgerEntry
{
    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string ActionType { get; set; } = string.Empty;

    public int Delta { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? ReferenceId { get; set; }

    public string? MetadataJson { get; set; }
}