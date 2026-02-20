using Hack13.Contracts.Enums;
using Hack13.Orchestrator;
using Hack13.TerminalClient;

// --- Quick connection-test mode ---
if (args.Length > 0 && args[0] == "--test-connection")
{
    if (!TryParseTestConnectionArgs(args, out var tcHost, out var tcPort, out var tcTls,
            out var tcCaCert, out var tcInsecure, out var tcTimeout))
    {
        PrintUsage();
        return 1;
    }
    return await TlsConnectionTest.RunAsync(tcHost!, tcPort, tcTls, tcCaCert, tcInsecure, timeoutSeconds: tcTimeout);
}

// --- Normal workflow mode ---
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
    Console.WriteLine();
    Console.WriteLine("  dotnet run --project src/Hack13.Cli -- --test-connection --host <host> --port <port> [--tls] [--ca-cert <path>] [--insecure] [--timeout <seconds>]");
    Console.WriteLine("");
    Console.WriteLine("  Example:");
    Console.WriteLine("     dotnet run --project src/Hack13.Cli -- --test-connection --host CMHDVP2.CLAYTON.NET --port 992 --tls --ca-cert certs/ClaytonRootCA.pem --timeout 20");
}

static bool TryParseTestConnectionArgs(
    string[] args,
    out string? host,
    out int port,
    out bool useTls,
    out string? caCertPath,
    out bool insecureSkipVerify,
    out int timeoutSeconds)
{
    host = null;
    port = 23;
    useTls = false;
    caCertPath = null;
    insecureSkipVerify = false;
    timeoutSeconds = 15;

    for (var i = 1; i < args.Length; i++) // skip args[0] which is --test-connection
    {
        switch (args[i])
        {
            case "--host" when i + 1 < args.Length:
                host = args[++i];
                break;
            case "--port" when i + 1 < args.Length:
                if (!int.TryParse(args[++i], out port)) return false;
                break;
            case "--tls":
                useTls = true;
                break;
            case "--ca-cert" when i + 1 < args.Length:
                caCertPath = args[++i];
                break;
            case "--insecure":
                insecureSkipVerify = true;
                break;
            case "--timeout" when i + 1 < args.Length:
                if (!int.TryParse(args[++i], out timeoutSeconds)) return false;
                break;
            default:
                return false;
        }
    }

    return !string.IsNullOrWhiteSpace(host);
}
