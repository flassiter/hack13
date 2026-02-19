namespace Hack13.Orchestrator;

public sealed class WorkflowOrchestratorOptions
{
    public Dictionary<string, string> EnvironmentSettings { get; init; } = [];
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
    public Func<Guid> IdGenerator { get; init; } = Guid.NewGuid;
    public Action<StepProgressUpdate>? ProgressCallback { get; init; }
}

public sealed class StepProgressUpdate
{
    public string WorkflowId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string ComponentType { get; init; } = string.Empty;
    public StepProgressState State { get; init; }
    public int Attempt { get; init; } = 1;
    public int MaxAttempts { get; init; } = 1;
    public string? Message { get; init; }
}

public enum StepProgressState
{
    Running,
    Succeeded,
    Failed,
    Skipped,
    Retrying
}
