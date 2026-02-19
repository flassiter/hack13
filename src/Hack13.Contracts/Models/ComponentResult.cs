using Hack13.Contracts.Enums;

namespace Hack13.Contracts.Models;

public class ComponentResult
{
    public ComponentStatus Status { get; set; }
    public Dictionary<string, string> OutputData { get; set; } = new();
    public ComponentError? Error { get; set; }
    public List<LogEntry> LogEntries { get; set; } = new();
    public long DurationMs { get; set; }
}

public class ComponentError
{
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string? StepDetail { get; set; }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string? WorkflowId { get; set; }
    public string? StepName { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object>? Data { get; set; }
}
