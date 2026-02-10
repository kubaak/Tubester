namespace Tubester.Worker;

public class WorkerOptions
{
    public int MaxDraftsPerRun { get; set; } = 25;
    public int IntervalSeconds { get; set; } = 100;
}