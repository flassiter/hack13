using Hack13.Contracts.Enums;

namespace Hack13.Contracts.Models;

public class WorkflowDefinition
{
    public string WorkflowId { get; set; } = string.Empty;
    public string WorkflowVersion { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> InitialParameters { get; set; } = new();
    public List<WorkflowStep> Steps { get; set; } = new();
}

public class WorkflowStep
{
    public string StepName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string ComponentConfig { get; set; } = string.Empty;
    public FailurePolicy OnFailure { get; set; } = FailurePolicy.Abort;
    public RetryConfig? Retry { get; set; }
    public ConditionDefinition? Condition { get; set; }
}

public class RetryConfig
{
    public int MaxAttempts { get; set; } = 1;
    public int BackoffSeconds { get; set; } = 5;
}

public class ConditionDefinition
{
    public string Key { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Min { get; set; }
    public string? Max { get; set; }
    public bool CaseSensitive { get; set; }
    public List<ConditionDefinition>? AllOf { get; set; }
    public List<ConditionDefinition>? AnyOf { get; set; }
    public ConditionDefinition? Not { get; set; }
}
