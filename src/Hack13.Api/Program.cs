using Hack13.Orchestrator;
using Hack13.EmailSender;
using System.Text.Json;
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

app.MapGet("/api/files/pdf", (string path) =>
{
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest(new { message = "path is required." });

    var fullPath = Path.GetFullPath(path);
    var outputRoot = Path.GetFullPath("output");
    if (!fullPath.StartsWith(outputRoot, StringComparison.Ordinal))
        return Results.BadRequest(new { message = "Only files under output/ are downloadable." });

    if (!File.Exists(fullPath))
        return Results.NotFound(new { message = "File not found." });

    return Results.File(fullPath, "application/pdf", enableRangeProcessing: true);
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", at = DateTimeOffset.UtcNow }));

app.Run();

static async Task WriteSseEventAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(payload);
    await response.WriteAsync($"event: {eventName}\n", cancellationToken);
    await response.WriteAsync($"data: {json}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

public sealed class ExecuteWorkflowRequest
{
    public Dictionary<string, string>? Parameters { get; set; }
}
