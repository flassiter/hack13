using System.Text.Json;
using System.Text.Json.Serialization;
using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;

namespace Hack13.Orchestrator;

public sealed class WorkflowLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly ComponentRegistry _registry;

    public WorkflowLoader(ComponentRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public WorkflowDefinition LoadFromFile(string workflowPath)
    {
        if (string.IsNullOrWhiteSpace(workflowPath))
            throw new ArgumentException("workflowPath is required.", nameof(workflowPath));
        if (!File.Exists(workflowPath))
            throw new FileNotFoundException($"Workflow definition not found: {workflowPath}");

        var json = File.ReadAllText(workflowPath);
        var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException("Workflow definition could not be parsed.");

        ValidateStructure(workflow);
        return workflow;
    }

    public void ValidateExecutionReadiness(
        WorkflowDefinition workflow,
        IReadOnlyDictionary<string, string> dataDictionary,
        string workflowPath)
    {
        foreach (var step in workflow.Steps)
        {
            if (string.Equals(step.ComponentType, "foreach", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var subStep in step.SubSteps ?? new List<WorkflowStep>())
                {
                    if (!_registry.IsRegistered(subStep.ComponentType))
                        throw new InvalidOperationException(
                            $"Sub-step '{subStep.StepName}' in foreach step '{step.StepName}' references unregistered component type '{subStep.ComponentType}'.");

                    var subPath = ResolveComponentConfigPath(subStep.ComponentConfig, dataDictionary, workflowPath);
                    if (!File.Exists(subPath))
                        throw new FileNotFoundException(
                            $"Sub-step '{subStep.StepName}' component config file not found: {subPath}");
                }
                continue;
            }

            if (!_registry.IsRegistered(step.ComponentType))
                throw new InvalidOperationException(
                    $"Step '{step.StepName}' references unregistered component type '{step.ComponentType}'.");

            var resolvedPath = ResolveComponentConfigPath(step.ComponentConfig, dataDictionary, workflowPath);
            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException(
                    $"Step '{step.StepName}' component config file not found: {resolvedPath}");
            }
        }
    }

    public static string ResolveComponentConfigPath(
        string configuredPath,
        IReadOnlyDictionary<string, string> dataDictionary,
        string workflowPath)
    {
        var resolved = PlaceholderResolver.Resolve(configuredPath, dataDictionary);
        if (Path.IsPathRooted(resolved))
            return resolved;

        var workflowDir = Path.GetDirectoryName(Path.GetFullPath(workflowPath))
            ?? Directory.GetCurrentDirectory();

        return Path.GetFullPath(Path.Combine(workflowDir, resolved));
    }

    private static void ValidateStructure(WorkflowDefinition workflow)
    {
        if (string.IsNullOrWhiteSpace(workflow.WorkflowId))
            throw new InvalidOperationException("workflow_id is required.");
        if (workflow.Steps.Count == 0)
            throw new InvalidOperationException("Workflow must define at least one step.");

        var uniqueStepNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in workflow.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.StepName))
                throw new InvalidOperationException("Each step must define step_name.");
            if (!uniqueStepNames.Add(step.StepName))
                throw new InvalidOperationException($"Duplicate step_name detected: '{step.StepName}'.");
            if (string.IsNullOrWhiteSpace(step.ComponentType))
                throw new InvalidOperationException($"Step '{step.StepName}' must define component_type.");

            if (string.Equals(step.ComponentType, "foreach", StringComparison.OrdinalIgnoreCase))
            {
                if (step.Foreach == null)
                    throw new InvalidOperationException($"Foreach step '{step.StepName}' must define a 'foreach' config block.");
                if (step.SubSteps == null || step.SubSteps.Count == 0)
                    throw new InvalidOperationException($"Foreach step '{step.StepName}' must define at least one sub_step.");

                var subStepNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var subStep in step.SubSteps)
                {
                    if (string.IsNullOrWhiteSpace(subStep.StepName))
                        throw new InvalidOperationException($"Each sub-step in foreach step '{step.StepName}' must define step_name.");
                    if (!subStepNames.Add(subStep.StepName))
                        throw new InvalidOperationException($"Duplicate sub-step name '{subStep.StepName}' in foreach step '{step.StepName}'.");
                    if (string.IsNullOrWhiteSpace(subStep.ComponentType))
                        throw new InvalidOperationException($"Sub-step '{subStep.StepName}' must define component_type.");
                    if (string.IsNullOrWhiteSpace(subStep.ComponentConfig))
                        throw new InvalidOperationException($"Sub-step '{subStep.StepName}' must define component_config.");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(step.ComponentConfig))
                    throw new InvalidOperationException($"Step '{step.StepName}' must define component_config.");
            }
        }
    }
}
