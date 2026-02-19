using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Interfaces;
using Hack13.Contracts.Models;

namespace Hack13.Orchestrator.Tests;

public class WorkflowOrchestratorTests : IDisposable
{
    private readonly string _tempDir;

    public WorkflowOrchestratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rpa-orchestrator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ExecuteAsync_MissingRequiredInitialParameter_Throws()
    {
        var workflowPath = WriteWorkflow("""
            {
              "workflow_id": "wf",
              "workflow_version": "1.0",
              "initial_parameters": ["loan_number"],
              "steps": [
                { "step_name": "s1", "component_type": "test_component", "component_config": "./s1.json" }
              ]
            }
            """);
        WriteJson("s1.json", """{ "status": "success" }""");

        var orchestrator = CreateOrchestrator(
            new ComponentRegistry().Register("test_component", () => new ScriptedComponent()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ExecuteAsync(workflowPath, new Dictionary<string, string>()));
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfullyRunsStepsAndMergesOutput()
    {
        var workflowPath = WriteWorkflow("""
            {
              "workflow_id": "wf-success",
              "workflow_version": "1.0",
              "initial_parameters": ["loan_number"],
              "steps": [
                { "step_name": "s1", "component_type": "test_component", "component_config": "./s1.json" },
                {
                  "step_name": "conditional_skip",
                  "component_type": "test_component",
                  "component_config": "./s2.json",
                  "condition": { "key": "run_extra", "operator": "equals", "value": "true" }
                }
              ]
            }
            """);
        WriteJson("s1.json", """{ "status": "success", "output_data": { "result_key": "value_{{loan_number}}" } }""");
        WriteJson("s2.json", """{ "status": "success", "output_data": { "should_not_exist": "x" } }""");

        var progress = new List<StepProgressUpdate>();
        var orchestrator = CreateOrchestrator(
            new ComponentRegistry().Register("test_component", () => new ScriptedComponent()),
            new WorkflowOrchestratorOptions
            {
                Clock = () => new DateTimeOffset(2026, 02, 19, 12, 0, 0, TimeSpan.Zero),
                IdGenerator = () => Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ProgressCallback = update => progress.Add(update)
            });

        var summary = await orchestrator.ExecuteAsync(workflowPath, new Dictionary<string, string>
        {
            ["loan_number"] = "1000001",
            ["run_extra"] = "false"
        });

        Assert.Equal(ComponentStatus.Success, summary.FinalStatus);
        Assert.Equal(2, summary.Steps.Count);
        Assert.Equal(ComponentStatus.Success, summary.Steps[0].Status);
        Assert.Equal(ComponentStatus.Skipped, summary.Steps[1].Status);
        Assert.Equal("value_1000001", summary.FinalDataDictionary["result_key"]);
        Assert.Equal("11111111-1111-1111-1111-111111111111", summary.ExecutionId);
        Assert.Equal("skipped", summary.FinalDataDictionary["_step_status"]);
        Assert.Contains(progress, x => x.StepName == "s1" && x.State == StepProgressState.Succeeded);
        Assert.Contains(progress, x => x.StepName == "conditional_skip" && x.State == StepProgressState.Skipped);
    }

    [Fact]
    public async Task ExecuteAsync_RetryPolicyRetriesThenSucceeds()
    {
        var workflowPath = WriteWorkflow("""
            {
              "workflow_id": "wf-retry",
              "workflow_version": "1.0",
              "steps": [
                {
                  "step_name": "retry_step",
                  "component_type": "retry_component",
                  "component_config": "./retry.json",
                  "on_failure": "retry",
                  "retry": { "max_attempts": 2, "backoff_seconds": 0 }
                }
              ]
            }
            """);
        WriteJson("retry.json", """{ "status": "ignored" }""");

        var retryComponent = new RetryThenSuccessComponent();
        var orchestrator = CreateOrchestrator(
            new ComponentRegistry().Register("retry_component", () => retryComponent));

        var summary = await orchestrator.ExecuteAsync(workflowPath, new Dictionary<string, string>());

        Assert.Equal(ComponentStatus.Success, summary.FinalStatus);
        Assert.Equal(2, retryComponent.AttemptCount);
        Assert.Equal(ComponentStatus.Success, summary.Steps[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_AbortPolicyStopsWorkflowOnFailure()
    {
        var workflowPath = WriteWorkflow("""
            {
              "workflow_id": "wf-abort",
              "workflow_version": "1.0",
              "steps": [
                { "step_name": "first", "component_type": "fail_component", "component_config": "./fail.json", "on_failure": "abort" },
                { "step_name": "second", "component_type": "test_component", "component_config": "./s2.json" }
              ]
            }
            """);
        WriteJson("fail.json", """{ "status": "failure", "error_code": "X1", "error_message": "boom" }""");
        WriteJson("s2.json", """{ "status": "success", "output_data": { "x": "1" } }""");

        var executed = false;
        var registry = new ComponentRegistry()
            .Register("fail_component", () => new ScriptedComponent())
            .Register("test_component", () => new CallbackComponent(() => executed = true));
        var orchestrator = CreateOrchestrator(registry);

        var summary = await orchestrator.ExecuteAsync(workflowPath, new Dictionary<string, string>());

        Assert.Equal(ComponentStatus.Failure, summary.FinalStatus);
        Assert.Single(summary.Steps);
        Assert.False(executed);
    }

    [Fact]
    public async Task ExecuteAsync_LogAndContinueContinuesAfterFailure()
    {
        var workflowPath = WriteWorkflow("""
            {
              "workflow_id": "wf-log-continue",
              "workflow_version": "1.0",
              "steps": [
                { "step_name": "first", "component_type": "fail_component", "component_config": "./fail.json", "on_failure": "log_and_continue" },
                { "step_name": "second", "component_type": "test_component", "component_config": "./s2.json" }
              ]
            }
            """);
        WriteJson("fail.json", """{ "status": "failure", "error_code": "X2", "error_message": "non-fatal" }""");
        WriteJson("s2.json", """{ "status": "success", "output_data": { "continued": "true" } }""");

        var registry = new ComponentRegistry()
            .Register("fail_component", () => new ScriptedComponent())
            .Register("test_component", () => new ScriptedComponent());
        var orchestrator = CreateOrchestrator(registry);

        var summary = await orchestrator.ExecuteAsync(workflowPath, new Dictionary<string, string>());

        Assert.Equal(ComponentStatus.Success, summary.FinalStatus);
        Assert.Equal(2, summary.Steps.Count);
        Assert.Equal(ComponentStatus.Failure, summary.Steps[0].Status);
        Assert.Equal(ComponentStatus.Success, summary.Steps[1].Status);
        Assert.Equal("true", summary.FinalDataDictionary["continued"]);
    }

    [Fact]
    public async Task ExecuteAsync_ComponentThrows_RecordsStepExceptionAndAborts()
    {
        var workflowPath = WriteWorkflow("""
            {
              "workflow_id": "wf-throws",
              "workflow_version": "1.0",
              "steps": [
                { "step_name": "explode", "component_type": "throw_component", "component_config": "./x.json", "on_failure": "abort" }
              ]
            }
            """);
        WriteJson("x.json", """{ "status": "ignored" }""");

        var registry = new ComponentRegistry()
            .Register("throw_component", () => new ThrowingComponent());

        var orchestrator = CreateOrchestrator(registry);
        var summary = await orchestrator.ExecuteAsync(workflowPath, new Dictionary<string, string>());

        Assert.Equal(ComponentStatus.Failure, summary.FinalStatus);
        Assert.Single(summary.Steps);
        Assert.Equal("STEP_EXCEPTION", summary.Steps[0].Error?.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_ConditionSupportsCompoundAndRangeEvaluation()
    {
        var workflowPath = WriteWorkflow("""
            {
              "workflow_id": "wf-compound-condition",
              "workflow_version": "1.0",
              "steps": [
                {
                  "step_name": "run_when_all_match",
                  "component_type": "test_component",
                  "component_config": "./s1.json",
                  "condition": {
                    "all_of": [
                      { "field": "notice_type", "operator": "equals", "value": "shortage" },
                      { "field": "escrow_shortage_surplus", "min": "-1000", "max": "-100" }
                    ]
                  }
                }
              ]
            }
            """);
        WriteJson("s1.json", """{ "status": "success", "output_data": { "executed": "true" } }""");

        var orchestrator = CreateOrchestrator(
            new ComponentRegistry().Register("test_component", () => new ScriptedComponent()));

        var summary = await orchestrator.ExecuteAsync(workflowPath, new Dictionary<string, string>
        {
            ["notice_type"] = "shortage",
            ["escrow_shortage_surplus"] = "-650.00"
        });

        Assert.Equal(ComponentStatus.Success, summary.FinalStatus);
        Assert.Single(summary.Steps);
        Assert.Equal(ComponentStatus.Success, summary.Steps[0].Status);
        Assert.Equal("true", summary.FinalDataDictionary["executed"]);
    }

    [Fact]
    public async Task ExecuteAsync_ConditionSupportsAnyOfNotAndCaseSensitiveStringChecks()
    {
        var workflowPath = WriteWorkflow("""
            {
              "workflow_id": "wf-advanced-string-condition",
              "workflow_version": "1.0",
              "steps": [
                {
                  "step_name": "advanced_condition_step",
                  "component_type": "test_component",
                  "component_config": "./s1.json",
                  "condition": {
                    "any_of": [
                      { "field": "status", "operator": "equals", "value": "Ready", "case_sensitive": true },
                      {
                        "not": { "key": "status", "operator": "contains", "value": "blocked" }
                      }
                    ]
                  }
                }
              ]
            }
            """);
        WriteJson("s1.json", """{ "status": "success", "output_data": { "ran": "yes" } }""");

        var orchestrator = CreateOrchestrator(
            new ComponentRegistry().Register("test_component", () => new ScriptedComponent()));

        var summary = await orchestrator.ExecuteAsync(workflowPath, new Dictionary<string, string>
        {
            ["status"] = "READY"
        });

        Assert.Equal(ComponentStatus.Success, summary.FinalStatus);
        Assert.Single(summary.Steps);
        Assert.Equal(ComponentStatus.Success, summary.Steps[0].Status);
        Assert.Equal("yes", summary.FinalDataDictionary["ran"]);
    }

    private WorkflowOrchestrator CreateOrchestrator(
        ComponentRegistry registry,
        WorkflowOrchestratorOptions? options = null) =>
        new(registry, options);

    private string WriteWorkflow(string json)
    {
        var path = Path.Combine(_tempDir, "workflow.json");
        File.WriteAllText(path, json);
        return path;
    }

    private void WriteJson(string relativePath, string json)
    {
        var path = Path.Combine(_tempDir, relativePath);
        File.WriteAllText(path, json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

internal sealed class ScriptedComponent : IComponent
{
    public string ComponentType => "test_component";

    public Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(config.Config.GetRawText());
        var root = doc.RootElement;
        var status = root.GetProperty("status").GetString() ?? "success";

        if (string.Equals(status, "failure", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ComponentResult
            {
                Status = ComponentStatus.Failure,
                Error = new ComponentError
                {
                    ErrorCode = root.TryGetProperty("error_code", out var code) ? code.GetString() ?? "ERR" : "ERR",
                    ErrorMessage = root.TryGetProperty("error_message", out var msg) ? msg.GetString() ?? "failure" : "failure"
                }
            });
        }

        var output = new Dictionary<string, string>();
        if (root.TryGetProperty("output_data", out var outputNode) && outputNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in outputNode.EnumerateObject())
                output[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }

        return Task.FromResult(new ComponentResult
        {
            Status = ComponentStatus.Success,
            OutputData = output
        });
    }
}

internal sealed class RetryThenSuccessComponent : IComponent
{
    public string ComponentType => "retry_component";
    public int AttemptCount { get; private set; }

    public Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default)
    {
        AttemptCount++;
        if (AttemptCount == 1)
        {
            return Task.FromResult(new ComponentResult
            {
                Status = ComponentStatus.Failure,
                Error = new ComponentError
                {
                    ErrorCode = "TRANSIENT",
                    ErrorMessage = "try again"
                }
            });
        }

        return Task.FromResult(new ComponentResult
        {
            Status = ComponentStatus.Success,
            OutputData = new Dictionary<string, string> { ["retried"] = "yes" }
        });
    }
}

internal sealed class CallbackComponent(Action onExecute) : IComponent
{
    private readonly Action _onExecute = onExecute;

    public string ComponentType => "callback";

    public Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default)
    {
        _onExecute();
        return Task.FromResult(new ComponentResult { Status = ComponentStatus.Success });
    }
}

internal sealed class ThrowingComponent : IComponent
{
    public string ComponentType => "throw_component";

    public Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("simulated crash");
    }
}
