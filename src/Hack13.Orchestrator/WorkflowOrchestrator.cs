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
                summary.Steps.Add(new StepExecutionSummary
                {
                    StepName = step.StepName,
                    ComponentType = step.ComponentType,
                    Status = ComponentStatus.Skipped,
                    DurationMs = 0
                });
                EmitProgress(workflow.WorkflowId, step, StepProgressState.Skipped);
                continue;
            }

            StepExecutionSummary stepSummary;
            if (string.Equals(step.ComponentType, "foreach", StringComparison.OrdinalIgnoreCase))
            {
                EmitProgress(workflow.WorkflowId, step, StepProgressState.Running);
                stepSummary = await ExecuteForeachAsync(step, workflowPath, workflow.WorkflowId, dataDictionary, cancellationToken);
                EmitProgress(
                    workflow.WorkflowId, step,
                    stepSummary.Status == ComponentStatus.Success ? StepProgressState.Succeeded : StepProgressState.Failed,
                    message: stepSummary.Error?.ErrorMessage);
            }
            else
            {
                stepSummary = await ExecuteStepWithRetryAsync(step, workflowPath, workflow.WorkflowId, dataDictionary, cancellationToken);
            }

            summary.Steps.Add(stepSummary);

            if (stepSummary.Status == ComponentStatus.Success)
            {
                dataDictionary["_step_status"] = "success";
                continue;
            }

            dataDictionary["_step_status"] = "failed";

            if (!string.Equals(step.ComponentType, "foreach", StringComparison.OrdinalIgnoreCase))
                EmitProgress(workflow.WorkflowId, step, StepProgressState.Failed, message: stepSummary.Error?.ErrorMessage);

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

    private async Task<StepExecutionSummary> ExecuteStepWithRetryAsync(
        WorkflowStep step,
        string workflowPath,
        string workflowId,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken)
    {
        var retryPolicy = GetRetrySettings(step);
        ComponentResult? lastResult = null;

        for (var attempt = 1; attempt <= retryPolicy.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stepTimer = Stopwatch.StartNew();
            EmitProgress(workflowId, step, StepProgressState.Running, attempt, retryPolicy.MaxAttempts);

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

                EmitProgress(workflowId, step, StepProgressState.Succeeded, attempt, retryPolicy.MaxAttempts);
                return new StepExecutionSummary
                {
                    StepName = step.StepName,
                    ComponentType = step.ComponentType,
                    Status = ComponentStatus.Success,
                    DurationMs = stepTimer.ElapsedMilliseconds + result.DurationMs
                };
            }

            lastResult = result;
            var canRetry = step.OnFailure == FailurePolicy.Retry && attempt < retryPolicy.MaxAttempts;
            if (canRetry)
            {
                EmitProgress(
                    workflowId, step, StepProgressState.Retrying,
                    attempt, retryPolicy.MaxAttempts,
                    result.Error?.ErrorMessage);

                if (retryPolicy.BackoffSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(retryPolicy.BackoffSeconds), cancellationToken);
                continue;
            }

            break;
        }

        var error = lastResult?.Error ?? new ComponentError
        {
            ErrorCode = "STEP_FAILED",
            ErrorMessage = "Step execution failed."
        };

        return new StepExecutionSummary
        {
            StepName = step.StepName,
            ComponentType = step.ComponentType,
            Status = ComponentStatus.Failure,
            DurationMs = lastResult?.DurationMs ?? 0,
            Error = error
        };
    }

    private async Task<StepExecutionSummary> ExecuteForeachAsync(
        WorkflowStep step,
        string workflowPath,
        string workflowId,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var foreachConfig = step.Foreach ?? new ForeachConfig();
        var subSteps = step.SubSteps ?? new List<WorkflowStep>();
        var iterations = new List<ForeachIterationSummary>();

        if (!dataDictionary.TryGetValue(foreachConfig.RowsKey, out var rowsJson)
            || string.IsNullOrWhiteSpace(rowsJson))
        {
            return new StepExecutionSummary
            {
                StepName = step.StepName,
                ComponentType = step.ComponentType,
                Status = ComponentStatus.Failure,
                DurationMs = sw.ElapsedMilliseconds,
                Error = new ComponentError
                {
                    ErrorCode = "FOREACH_ROWS_MISSING",
                    ErrorMessage = $"Data dictionary key '{foreachConfig.RowsKey}' is missing or empty."
                }
            };
        }

        List<Dictionary<string, string>> rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(rowsJson)
                   ?? new List<Dictionary<string, string>>();
        }
        catch (JsonException ex)
        {
            return new StepExecutionSummary
            {
                StepName = step.StepName,
                ComponentType = step.ComponentType,
                Status = ComponentStatus.Failure,
                DurationMs = sw.ElapsedMilliseconds,
                Error = new ComponentError
                {
                    ErrorCode = "FOREACH_ROWS_INVALID",
                    ErrorMessage = $"Failed to deserialize rows from '{foreachConfig.RowsKey}': {ex.Message}"
                }
            };
        }

        var rowPrefix = foreachConfig.RowPrefix ?? string.Empty;
        var rowCount = rows.Count;
        var overallStatus = ComponentStatus.Success;
        ComponentError? firstError = null;

        for (var i = 0; i < rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Isolated copy — sub-step output never leaks to the main dictionary or other iterations
            var scopedDict = new Dictionary<string, string>(dataDictionary, StringComparer.OrdinalIgnoreCase);
            scopedDict["_foreach_index"] = i.ToString();
            scopedDict["_foreach_count"] = rowCount.ToString();

            foreach (var (colKey, colValue) in rows[i])
                scopedDict[$"{rowPrefix}{colKey}"] = colValue;

            var iterationSw = Stopwatch.StartNew();
            var iterationSteps = new List<StepExecutionSummary>();
            var iterationStatus = ComponentStatus.Success;
            ComponentError? iterationError = null;

            foreach (var subStep in subSteps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scopedDict["_step_name"] = subStep.StepName;

                if (subStep.Condition != null && !ConditionEvaluator.Evaluate(subStep.Condition, scopedDict))
                {
                    scopedDict["_step_status"] = "skipped";
                    iterationSteps.Add(new StepExecutionSummary
                    {
                        StepName = subStep.StepName,
                        ComponentType = subStep.ComponentType,
                        Status = ComponentStatus.Skipped,
                        DurationMs = 0
                    });
                    continue;
                }

                var subStepSummary = await ExecuteStepWithRetryAsync(
                    subStep, workflowPath, workflowId, scopedDict, cancellationToken);
                iterationSteps.Add(subStepSummary);

                if (subStepSummary.Status != ComponentStatus.Success)
                {
                    scopedDict["_step_status"] = "failed";
                    if (subStep.OnFailure == FailurePolicy.LogAndContinue)
                        continue;

                    // Sub-step aborted this iteration
                    iterationStatus = ComponentStatus.Failure;
                    iterationError = subStepSummary.Error;
                    break;
                }

                scopedDict["_step_status"] = "success";
            }

            iterations.Add(new ForeachIterationSummary
            {
                RowIndex = i,
                Status = iterationStatus,
                DurationMs = iterationSw.ElapsedMilliseconds,
                Steps = iterationSteps
            });

            if (iterationStatus == ComponentStatus.Failure)
            {
                firstError ??= iterationError;
                overallStatus = ComponentStatus.Failure;

                // Abort the foreach immediately unless the step says to log and continue
                if (step.OnFailure != FailurePolicy.LogAndContinue)
                    break;
            }
        }

        sw.Stop();

        return new StepExecutionSummary
        {
            StepName = step.StepName,
            ComponentType = step.ComponentType,
            Status = overallStatus,
            DurationMs = sw.ElapsedMilliseconds,
            Error = overallStatus == ComponentStatus.Failure
                ? (firstError ?? new ComponentError
                {
                    ErrorCode = "FOREACH_FAILED",
                    ErrorMessage = "One or more foreach iterations failed."
                })
                : null,
            Iterations = iterations
        };
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
