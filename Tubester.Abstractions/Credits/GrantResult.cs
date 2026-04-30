namespace Tubester.Abstractions.Credits;

public sealed class GrantResult
{
    public bool Granted { get; set; }
    public bool AlreadyProcessed { get; set; }

    public int NewBalance { get; set; }

    public string? Reason { get; set; }
}
