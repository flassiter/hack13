using Hack13.Orchestrator;
using Hack13.EmailSender;
using Hack13.Contracts.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseCors("frontend");
app.UseHttpsRedirection();

app.MapGet("/api/workflows", (IConfiguration config) =>
{
    var workflowsDirectory = config["Workflow:Directory"] ?? "configs/workflows";
    if (!Directory.Exists(workflowsDirectory))
        return Results.Ok(Array.Empty<WorkflowMetadata>());

    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    var workflows = new List<WorkflowMetadata>();
    foreach (var path in Directory.EnumerateFiles(workflowsDirectory, "*.json", SearchOption.TopDirectoryOnly))
    {
        var fileId = Path.GetFileNameWithoutExtension(path);
        try
        {
            var json = File.ReadAllText(path);
            var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(json, jsonOptions)
                ?? throw new InvalidOperationException("Workflow definition could not be parsed.");

            var initialParameters = workflow.InitialParameters
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var stepNames = workflow.Steps
                .Select(step => step.StepName)
                .Where(stepName => !string.IsNullOrWhiteSpace(stepName))
                .ToArray();

            var componentInfo = CollectComponentInfo(workflow, path);

            workflows.Add(new WorkflowMetadata
            {
                Id = fileId,
                WorkflowId = string.IsNullOrWhiteSpace(workflow.WorkflowId) ? fileId : workflow.WorkflowId,
                WorkflowVersion = workflow.WorkflowVersion,
                Description = workflow.Description,
                LastModified = File.GetLastWriteTimeUtc(path),
                InitialParameters = initialParameters,
                StepCount = stepNames.Length,
                StepNames = stepNames,
                ComponentTypes = componentInfo.ComponentTypes
                    .OrderBy(ct => ct, StringComparer.OrdinalIgnoreCase).ToArray(),
                PdfTemplates = componentInfo.PdfTemplates
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                HasEmailSender = componentInfo.HasEmailSender
            });
        }
        catch (Exception ex)
        {
            workflows.Add(new WorkflowMetadata
            {
                Id = fileId,
                WorkflowId = fileId,
                LastModified = File.GetLastWriteTimeUtc(path),
                ParseError = true,
                ParseErrorMessage = ex.Message
            });
        }
    }

    return Results.Ok(workflows.OrderBy(workflow => workflow.Id, StringComparer.OrdinalIgnoreCase));
});

static WorkflowOrchestrator CreateOrchestrator(IConfiguration config, Action<StepProgressUpdate>? callback = null)
{
    return new WorkflowOrchestrator(
        registry: ComponentRegistry.CreateDefault(
            config.GetSection("EmailSender").Get<EmailSenderEnvironmentConfig>()),
        options: new WorkflowOrchestratorOptions
        {
            EnvironmentSettings = config
                .GetSection("Workflow:EnvironmentSettings")
                .Get<Dictionary<string, string>>() ?? new Dictionary<string, string>(),
            ProgressCallback = callback
        });
}

app.MapPost("/api/workflows/{workflowId}/execute", async (
    string workflowId,
    ExecuteWorkflowRequest request,
    IConfiguration config,
    CancellationToken cancellationToken) =>
{
    var workflowsDirectory = config["Workflow:Directory"] ?? "configs/workflows";
    var workflowPath = WorkflowPathResolver.ResolveById(workflowsDirectory, workflowId);
    if (workflowPath == null)
        return Results.NotFound(new { message = $"Workflow '{workflowId}' not found." });

    var orchestrator = CreateOrchestrator(config);

    try
    {
        var summary = await orchestrator.ExecuteAsync(
            workflowPath,
            request.Parameters ?? new Dictionary<string, string>(),
            cancellationToken);

        return Results.Ok(summary);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            message = ex.Message
        });
    }
});

app.MapGet("/api/workflows/{workflowId}/execute-stream", async (
    string workflowId,
    HttpRequest request,
    HttpResponse response,
    IConfiguration config,
    CancellationToken cancellationToken) =>
{
    response.Headers.CacheControl = "no-cache";
    response.Headers.Connection = "keep-alive";
    response.ContentType = "text/event-stream";

    var workflowsDirectory = config["Workflow:Directory"] ?? "configs/workflows";
    var workflowPath = WorkflowPathResolver.ResolveById(workflowsDirectory, workflowId);
    if (workflowPath == null)
    {
        await WriteSseEventAsync(response, "workflow_error", new { message = $"Workflow '{workflowId}' not found." }, cancellationToken);
        return;
    }

    var parameters = request.Query
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString(), StringComparer.OrdinalIgnoreCase);

    var channel = Channel.CreateUnbounded<StepProgressUpdate>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    var orchestrator = CreateOrchestrator(config, update => channel.Writer.TryWrite(update));
    var executeTask = Task.Run(async () =>
    {
        try
        {
            var summary = await orchestrator.ExecuteAsync(workflowPath, parameters, cancellationToken);
            return (Summary: summary, Error: (Exception?)null);
        }
        catch (Exception ex)
        {
            return (Summary: (Hack13.Contracts.Models.WorkflowExecutionSummary?)null, Error: ex);
        }
        finally
        {
            channel.Writer.TryComplete();
        }
    }, cancellationToken);

    try
    {
        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
        {
            await WriteSseEventAsync(response, "progress", new
            {
                stepName = update.StepName,
                componentType = update.ComponentType,
                state = update.State.ToString(),
                attempt = update.Attempt,
                maxAttempts = update.MaxAttempts,
                message = update.Message
            }, cancellationToken);
        }

        var (summary, error) = await executeTask;
        if (error != null)
        {
            await WriteSseEventAsync(response, "workflow_error", new { message = error.Message }, cancellationToken);
            return;
        }

        await WriteSseEventAsync(response, "summary", summary!, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        // Client disconnected.
    }
});

app.MapGet("/api/workflows/{workflowId}/definition", (string workflowId, IConfiguration config) =>
{
    var workflowsDirectory = config["Workflow:Directory"] ?? "configs/workflows";
    var workflowPath = WorkflowPathResolver.ResolveById(workflowsDirectory, workflowId);
    if (workflowPath == null)
        return Results.NotFound(new { message = $"Workflow '{workflowId}' not found." });

    var json = File.ReadAllText(workflowPath);
    return Results.Text(json, "application/json");
});

app.MapPut("/api/workflows/{workflowId}/definition", async (string workflowId, HttpRequest request, IConfiguration config) =>
{
    var workflowsDirectory = config["Workflow:Directory"] ?? "configs/workflows";
    var workflowPath = WorkflowPathResolver.ResolveById(workflowsDirectory, workflowId);
    if (workflowPath == null)
        return Results.NotFound(new { message = $"Workflow '{workflowId}' not found." });

    string body;
    using (var reader = new StreamReader(request.Body))
    {
        body = await reader.ReadToEndAsync();
    }

    if (string.IsNullOrWhiteSpace(body))
        return Results.BadRequest(new { message = "Request body must contain valid JSON." });

    // Validate it's valid JSON
    try
    {
        using var doc = JsonDocument.Parse(body);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { message = $"Invalid JSON: {ex.Message}" });
    }

    await File.WriteAllTextAsync(workflowPath, body);
    return Results.Ok(new { message = "Workflow saved.", workflowId });
});

app.MapGet("/api/files/pdf", (string path) =>
{
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest(new { message = "path is required." });

    var fullPath = Path.GetFullPath(path);
    var outputRoot = Path.GetFullPath("output");
    if (!IsPathWithinDirectory(fullPath, outputRoot))
        return Results.BadRequest(new { message = "Only files under output/ are downloadable." });

    if (!File.Exists(fullPath))
        return Results.NotFound(new { message = "File not found." });

    return Results.File(fullPath, "application/pdf", enableRangeProcessing: true);
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", at = DateTimeOffset.UtcNow }));

app.Run();

static WorkflowComponentSummary CollectComponentInfo(WorkflowDefinition workflow, string workflowPath)
{
    var summary = new WorkflowComponentSummary();
    CollectFromSteps(workflow.Steps, workflowPath, summary);
    return summary;
}

static void CollectFromSteps(List<WorkflowStep> steps, string workflowPath, WorkflowComponentSummary summary)
{
    foreach (var step in steps)
    {
        var ct = step.ComponentType?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(ct))
            summary.ComponentTypes.Add(ct);

        if (string.Equals(ct, "email_sender", StringComparison.OrdinalIgnoreCase))
            summary.HasEmailSender = true;

        if (string.Equals(ct, "pdf_generator", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(step.ComponentConfig))
        {
            var templateId = TryExtractPdfTemplateId(step.ComponentConfig, workflowPath);
            if (!string.IsNullOrWhiteSpace(templateId) && !templateId.Contains("{{"))
                summary.PdfTemplates.Add(templateId);
        }

        if (step.SubSteps?.Count > 0)
            CollectFromSteps(step.SubSteps, workflowPath, summary);
    }
}

static string? TryExtractPdfTemplateId(string componentConfigRelPath, string workflowPath)
{
    try
    {
        var workflowDir = Path.GetDirectoryName(Path.GetFullPath(workflowPath));
        if (workflowDir == null) return null;
        var configPath = Path.GetFullPath(Path.Combine(workflowDir, componentConfigRelPath));
        if (!File.Exists(configPath)) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = doc.RootElement;

        // Envelope format: { "config": { "template_id": "..." } }
        if (root.TryGetProperty("config", out var configEl)
            && configEl.TryGetProperty("template_id", out var tid))
            return tid.GetString();

        // Direct format: { "template_id": "..." }
        if (root.TryGetProperty("template_id", out var directTid))
            return directTid.GetString();

        return null;
    }
    catch
    {
        return null;
    }
}

static async Task WriteSseEventAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(payload);
    await response.WriteAsync($"event: {eventName}\n", cancellationToken);
    await response.WriteAsync($"data: {json}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

static bool IsPathWithinDirectory(string candidatePath, string directoryPath)
{
    var fullDirectoryPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;
    var fullCandidatePath = Path.GetFullPath(candidatePath);
    var comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    return fullCandidatePath.StartsWith(fullDirectoryPath, comparison);
}

public sealed class ExecuteWorkflowRequest
{
    public Dictionary<string, string>? Parameters { get; set; }
}

public sealed class WorkflowMetadata
{
    public string Id { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public string WorkflowVersion { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string[] InitialParameters { get; set; } = [];
    public int StepCount { get; set; }
    public string[] StepNames { get; set; } = [];
    public string[] ComponentTypes { get; set; } = [];
    public string[] PdfTemplates { get; set; } = [];
    public bool HasEmailSender { get; set; }
    public bool ParseError { get; set; }
    public string? ParseErrorMessage { get; set; }
}

sealed class WorkflowComponentSummary
{
    public HashSet<string> ComponentTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> PdfTemplates { get; } = [];
    public bool HasEmailSender { get; set; }
}
