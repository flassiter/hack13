using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Hack13.Contracts.Models;
using Hack13.Orchestrator;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hack13.Api.Services;

public class BedrockService : IDisposable
{
    private readonly string _modelId;
    private readonly AmazonBedrockRuntimeClient _client;
    private readonly ILogger<BedrockService> _logger;

    private const string SystemPrompt =
        "You are an expert at explaining automated business workflows to non-technical business stakeholders. " +
        "When given a workflow definition and its component configurations, explain clearly what the workflow does " +
        "in plain English. Structure your explanation with: a short summary paragraph, then a numbered list of steps " +
        "describing what each step does and why, and finally a sentence about the business outcome. " +
        "Avoid technical jargon. Do not repeat raw JSON. Be concise but thorough.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public BedrockService(IConfiguration configuration, ILogger<BedrockService> logger)
    {
        _logger = logger;
        var region = configuration["Bedrock:Region"] ?? "us-east-1";
        _modelId = configuration["Bedrock:ModelId"] ?? "us.anthropic.claude-4-5-sonnet-20250929-v1:0";
        _client = new AmazonBedrockRuntimeClient(RegionEndpoint.GetBySystemName(region));
        _logger.LogInformation("BedrockService initialized. Region: {Region}, Model: {ModelId}", region, _modelId);
    }

    public async Task<string> ExplainWorkflowAsync(string workflowPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Explaining workflow {WorkflowPath}", workflowPath);

        var workflowJson = await File.ReadAllTextAsync(workflowPath, cancellationToken);
        var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(workflowJson, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse workflow definition.");

        var context = BuildContext(workflowPath, workflowJson, workflow);

        var request = new ConverseRequest
        {
            ModelId = _modelId,
            System = [new SystemContentBlock { Text = SystemPrompt }],
            Messages =
            [
                new Message
                {
                    Role = ConversationRole.User,
                    Content = [new ContentBlock { Text = $"Please explain what this workflow does:\n\n{context}" }]
                }
            ],
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = 1024,
                Temperature = 0.3f
            }
        };

        try
        {
            var response = await _client.ConverseAsync(request, cancellationToken);
            _logger.LogInformation(
                "Explain succeeded for {WorkflowPath}. Input tokens: {InputTokens}, Output tokens: {OutputTokens}",
                workflowPath, response.Usage?.InputTokens, response.Usage?.OutputTokens);
            return response.Output.Message.Content[0].Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bedrock call failed for workflow {WorkflowPath}", workflowPath);
            throw;
        }
    }

    private static string BuildContext(string workflowPath, string workflowJson, WorkflowDefinition workflow)
    {
        var componentSections = new List<string>();

        CollectSteps(workflow.Steps, workflowPath, componentSections);

        var sb = new StringBuilder();
        sb.AppendLine("## Workflow Definition");
        sb.AppendLine($"```json\n{workflowJson}\n```");
        sb.AppendLine();

        if (componentSections.Count > 0)
        {
            sb.AppendLine("## Component Configurations");
            foreach (var section in componentSections)
            {
                sb.AppendLine(section);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static void CollectSteps(IEnumerable<WorkflowStep> steps, string workflowPath, List<string> sections)
    {
        foreach (var step in steps)
        {
            if (!string.IsNullOrWhiteSpace(step.ComponentConfig))
            {
                try
                {
                    var componentPath = WorkflowLoader.ResolveComponentConfigPath(
                        step.ComponentConfig,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        workflowPath);
                    if (File.Exists(componentPath))
                    {
                        var componentJson = File.ReadAllText(componentPath);
                        sections.Add($"### Component config for step \"{step.StepName}\" ({step.ComponentType})\n```json\n{componentJson}\n```");
                    }
                }
                catch (InvalidOperationException)
                {
                    // Skip component configs that resolve outside of allowed workflow config roots.
                }
            }

            if (step.SubSteps is { Count: > 0 })
                CollectSteps(step.SubSteps, workflowPath, sections);
        }
    }

    public void Dispose() => _client.Dispose();
}
