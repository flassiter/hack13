using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;

namespace Hack13.Orchestrator;

public sealed class WorkflowOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly ComponentRegistry _registry;
    private readonly WorkflowLoader _loader;
    private readonly WorkflowOrchestratorOptions _options;

    public WorkflowOrchestrator(
        ComponentRegistry? registry = null,
        WorkflowOrchestratorOptions? options = null)
    {
        _registry = registry ?? ComponentRegistry.CreateDefault();
        _loader = new WorkflowLoader(_registry);
        _options = options ?? new WorkflowOrchestratorOptions();
    }

    public async Task<WorkflowExecutionSummary> ExecuteAsync(
        string workflowPath,
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        var workflow = _loader.LoadFromFile(workflowPath);
        var dataDictionary = InitializeDataDictionary(workflow, parameters);
        _loader.ValidateExecutionReadiness(workflow, dataDictionary, workflowPath);

        var startedAt = _options.Clock();
        var summary = new WorkflowExecutionSummary
        {
            WorkflowId = workflow.WorkflowId,
            ExecutionId = dataDictionary["_workflow_id"],
            StartedAt = startedAt.UtcDateTime,
            FinalStatus = ComponentStatus.Success
        };

        foreach (var step in workflow.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dataDictionary["_step_name"] = step.StepName;

            if (step.Condition != null && !ConditionEvaluator.Evaluate(step.Condition, dataDictionary))
            {
                dataDictionary["_step_status"] = "skipped";
                var skippedSummary = new StepExecutionSummary
                {
                    StepName = step.StepName,
                    ComponentType = step.ComponentType,
                    Status = ComponentStatus.Skipped,
                    DurationMs = 0
                };
                summary.Steps.Add(skippedSummary);
                EmitProgress(workflow.WorkflowId, step, StepProgressState.Skipped);
                continue;
            }

            var retryPolicy = GetRetrySettings(step);
            var didSucceed = false;
            ComponentResult? lastFailure = null;

            for (var attempt = 1; attempt <= retryPolicy.MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stepTimer = Stopwatch.StartNew();
                EmitProgress(workflow.WorkflowId, step, StepProgressState.Running, attempt, retryPolicy.MaxAttempts);
                ComponentResult result;
                try
                {
                    var componentConfig = LoadComponentConfiguration(step, workflowPath, dataDictionary);
                    var component = _registry.Create(step.ComponentType);
                    result = await component.ExecuteAsync(componentConfig, dataDictionary, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result = new ComponentResult
                    {
                        Status = ComponentStatus.Failure,
                        Error = new ComponentError
                        {
                            ErrorCode = "STEP_EXCEPTION",
                            ErrorMessage = ex.Message,
                            StepDetail = step.StepName
                        }
                    };
                }
                finally
                {
                    stepTimer.Stop();
                }

                if (result.Status == ComponentStatus.Success)
                {
                    foreach (var (key, value) in result.OutputData)
                        dataDictionary[key] = value;

                    dataDictionary["_step_status"] = "success";
                    summary.Steps.Add(new StepExecutionSummary
                    {
                        StepName = step.StepName,
                        ComponentType = step.ComponentType,
                        Status = ComponentStatus.Success,
                        DurationMs = stepTimer.ElapsedMilliseconds + result.DurationMs,
                        Error = result.Error
                    });
                    EmitProgress(workflow.WorkflowId, step, StepProgressState.Succeeded, attempt, retryPolicy.MaxAttempts);
                    didSucceed = true;
                    break;
                }

                lastFailure = result;
                var canRetry = step.OnFailure == FailurePolicy.Retry && attempt < retryPolicy.MaxAttempts;
                if (canRetry)
                {
                    EmitProgress(
                        workflow.WorkflowId,
                        step,
                        StepProgressState.Retrying,
                        attempt,
                        retryPolicy.MaxAttempts,
                        result.Error?.ErrorMessage);

                    if (retryPolicy.BackoffSeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(retryPolicy.BackoffSeconds), cancellationToken);
                    continue;
                }

                break;
            }

            if (didSucceed)
                continue;

            var error = lastFailure?.Error ?? new ComponentError
            {
                ErrorCode = "STEP_FAILED",
                ErrorMessage = "Step execution failed."
            };

            var failedSummary = new StepExecutionSummary
            {
                StepName = step.StepName,
                ComponentType = step.ComponentType,
                Status = ComponentStatus.Failure,
                DurationMs = lastFailure?.DurationMs ?? 0,
                Error = error
            };

            summary.Steps.Add(failedSummary);
            dataDictionary["_step_status"] = "failed";
            EmitProgress(workflow.WorkflowId, step, StepProgressState.Failed, message: error.ErrorMessage);

            if (step.OnFailure == FailurePolicy.LogAndContinue)
                continue;

            summary.FinalStatus = ComponentStatus.Failure;
            summary.CompletedAt = _options.Clock().UtcDateTime;
            summary.FinalDataDictionary = new Dictionary<string, string>(dataDictionary);
            return summary;
        }

        summary.CompletedAt = _options.Clock().UtcDateTime;
        summary.FinalDataDictionary = new Dictionary<string, string>(dataDictionary);
        return summary;
    }

    private Dictionary<string, string> InitializeDataDictionary(
        WorkflowDefinition workflow,
        Dictionary<string, string> parameters)
    {
        var data = new Dictionary<string, string>(_options.EnvironmentSettings, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
            data[key] = value;

        foreach (var required in workflow.InitialParameters)
        {
            if (!data.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    $"Initial parameter '{required}' is required and must be non-empty.");
        }

        data["_workflow_id"] = _options.IdGenerator().ToString();
        data["_started_at"] = _options.Clock().ToString("O");
        return data;
    }

    private ComponentConfiguration LoadComponentConfiguration(
        WorkflowStep step,
        string workflowPath,
        Dictionary<string, string> dataDictionary)
    {
        var configPath = WorkflowLoader.ResolveComponentConfigPath(step.ComponentConfig, dataDictionary, workflowPath);
        var json = File.ReadAllText(configPath);
        var resolvedJson = PlaceholderResolver.Resolve(json, dataDictionary);

        using var document = JsonDocument.Parse(resolvedJson);
        var root = document.RootElement;

        if (TryParseEnvelope(root, out var envelope))
        {
            if (!string.Equals(envelope.ComponentType, step.ComponentType, StringComparison.OrdinalIgnoreCase))
            {
                envelope.ComponentType = step.ComponentType;
            }

            return envelope;
        }

        return new ComponentConfiguration
        {
            ComponentType = step.ComponentType,
            ComponentVersion = "1.0",
            Description = $"Loaded from {configPath}",
            Config = root.Clone()
        };
    }

    private static bool TryParseEnvelope(JsonElement root, out ComponentConfiguration configuration)
    {
        configuration = default!;
        if (!root.TryGetProperty("component_type", out _))
            return false;
        if (!root.TryGetProperty("config", out _))
            return false;

        configuration = root.Deserialize<ComponentConfiguration>(JsonOptions)
            ?? throw new InvalidOperationException("Component configuration envelope is invalid.");
        return true;
    }

    private static (int MaxAttempts, int BackoffSeconds) GetRetrySettings(WorkflowStep step)
    {
        if (step.OnFailure != FailurePolicy.Retry)
            return (1, 0);

        var maxAttempts = Math.Max(step.Retry?.MaxAttempts ?? 3, 1);
        var backoffSeconds = Math.Max(step.Retry?.BackoffSeconds ?? 1, 0);
        return (maxAttempts, backoffSeconds);
    }

    private void EmitProgress(
        string workflowId,
        WorkflowStep step,
        StepProgressState state,
        int attempt = 1,
        int maxAttempts = 1,
        string? message = null)
    {
        _options.ProgressCallback?.Invoke(new StepProgressUpdate
        {
            WorkflowId = workflowId,
            StepName = step.StepName,
            ComponentType = step.ComponentType,
            State = state,
            Attempt = attempt,
            MaxAttempts = maxAttempts,
            Message = message
        });
    }
}
