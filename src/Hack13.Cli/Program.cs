using Hack13.Contracts.Enums;
using Hack13.Orchestrator;

if (!TryParseArgs(args, out var workflowPath, out var parameters))
{
    PrintUsage();
    return 1;
}

var orchestrator = new WorkflowOrchestrator(
    options: new WorkflowOrchestratorOptions
    {
        ProgressCallback = PrintProgress
    });

try
{
    var summary = await orchestrator.ExecuteAsync(workflowPath!, parameters);
    PrintSummary(summary);
    return summary.FinalStatus == ComponentStatus.Success ? 0 : 2;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Workflow execution failed: {ex.Message}");
    Console.ResetColor();
    return 2;
}

static bool TryParseArgs(
    string[] args,
    out string? workflowPath,
    out Dictionary<string, string> parameters)
{
    workflowPath = null;
    parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];

        if (arg == "--workflow" && i + 1 < args.Length)
        {
            workflowPath = args[++i];
            continue;
        }

        if (arg == "--param" && i + 1 < args.Length)
        {
            var pair = args[++i];
            var splitIndex = pair.IndexOf('=');
            if (splitIndex <= 0 || splitIndex == pair.Length - 1)
                return false;

            var key = pair[..splitIndex];
            var value = pair[(splitIndex + 1)..];
            parameters[key] = value;
            continue;
        }

        return false;
    }

    return !string.IsNullOrWhiteSpace(workflowPath);
}

static void PrintProgress(StepProgressUpdate update)
{
    var original = Console.ForegroundColor;
    Console.ForegroundColor = update.State switch
    {
        StepProgressState.Succeeded => ConsoleColor.Green,
        StepProgressState.Failed => ConsoleColor.Red,
        StepProgressState.Skipped => ConsoleColor.DarkYellow,
        StepProgressState.Retrying => ConsoleColor.Yellow,
        _ => ConsoleColor.Cyan
    };

    var attemptInfo = update.MaxAttempts > 1 ? $" (attempt {update.Attempt}/{update.MaxAttempts})" : string.Empty;
    var message = string.IsNullOrWhiteSpace(update.Message) ? string.Empty : $" - {update.Message}";
    Console.WriteLine($"[{update.State}] {update.StepName}{attemptInfo}{message}");
    Console.ForegroundColor = original;
}

static void PrintSummary(Hack13.Contracts.Models.WorkflowExecutionSummary summary)
{
    Console.WriteLine();
    Console.WriteLine($"Workflow: {summary.WorkflowId}");
    Console.WriteLine($"Execution: {summary.ExecutionId}");
    Console.WriteLine($"Status: {summary.FinalStatus}");
    Console.WriteLine("Steps:");

    foreach (var step in summary.Steps)
    {
        Console.WriteLine($" - {step.StepName} [{step.Status}] ({step.DurationMs} ms)");
        if (step.Error != null)
            Console.WriteLine($"   error: {step.Error.ErrorCode} - {step.Error.ErrorMessage}");
    }

    Console.WriteLine("Final data dictionary:");
    foreach (var pair in summary.FinalDataDictionary.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        Console.WriteLine($" - {pair.Key}={pair.Value}");
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/Hack13.Cli -- --workflow <path> [--param key=value]...");
}
