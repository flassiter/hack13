using Hack13.Contracts.Enums;

namespace Hack13.Contracts.Models;

public class WorkflowExecutionSummary
{
    public string WorkflowId { get; set; } = string.Empty;
    public string ExecutionId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ComponentStatus FinalStatus { get; set; }
    public List<StepExecutionSummary> Steps { get; set; } = new();
    public Dictionary<string, string> FinalDataDictionary { get; set; } = new();
}

public class StepExecutionSummary
{
    public string StepName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public ComponentStatus Status { get; set; }
    public long DurationMs { get; set; }
    public ComponentError? Error { get; set; }
    public List<ForeachIterationSummary>? Iterations { get; set; }
}

public class ForeachIterationSummary
{
    public int RowIndex { get; set; }
    public ComponentStatus Status { get; set; }
    public long DurationMs { get; set; }
    public List<StepExecutionSummary> Steps { get; set; } = new();
}
