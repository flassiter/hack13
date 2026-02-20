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

    private const string ExplainSystemPrompt =
        "You are an expert at explaining automated business workflows to non-technical business stakeholders. " +
        "When given a workflow definition and its component configurations, explain clearly what the workflow does " +
        "in plain English. Structure your explanation with: a short summary paragraph, then a numbered list of steps " +
        "describing what each step does and why, and finally a sentence about the business outcome. " +
        "Avoid technical jargon. Do not repeat raw JSON. Be concise but thorough.";

    private const string SuggestSystemPrompt =
        "You are an expert at modifying automated business workflow configuration files. " +
        "When given a set of workflow and component JSON files, their schema documentation, and a change request, " +
        "output only the files that need to be modified with their complete new content.\n\n" +
        "Format your response exactly as follows:\n" +
        "1. Start with a brief paragraph explaining what changes you are making and why.\n" +
        "2. For each file that needs to change, output a labeled block:\n\n" +
        "=== FILE: <exact-relative-path-as-shown-in-input> ===\n" +
        "```json\n" +
        "<complete new file content>\n" +
        "```\n\n" +
        "Rules:\n" +
        "- Only include files that actually need to change.\n" +
        "- Use the exact relative file paths shown in the input (e.g. configs/workflows/my_workflow.json).\n" +
        "- Output the complete file content for each changed file, not just the changed portions.\n" +
        "- All JSON must be valid and well-formatted with 2-space indentation.\n" +
        "- Do not add commentary outside the described format.";

    private const string SchemaAndComponentReference = """
        ## Workflow Schema Reference

        ### WorkflowDefinition
        ```json
        {
          "workflow_id": "string – unique identifier",
          "workflow_version": "string – e.g. \"1.0\"",
          "description": "string – human-readable description",
          "initial_parameters": ["param_name"],
          "steps": [ WorkflowStep ]
        }
        ```

        ### WorkflowStep
        ```json
        {
          "step_name": "string – unique name for this step",
          "component_type": "string – see Available Component Types below",
          "component_config": "relative/path/to/component.json",
          "on_failure": "abort | retry | log_and_continue",
          "retry": { "max_attempts": 3, "backoff_seconds": 5 },
          "condition": ConditionDefinition,
          "foreach": { "rows_key": "db_rows", "row_prefix": "loan_" },
          "sub_steps": [ WorkflowStep ]
        }
        ```
        Fields:
        - `on_failure`: `abort` (default, stop workflow), `retry` (use retry config then abort), `log_and_continue` (log error and proceed)
        - `retry`: optional, only used when on_failure is `retry`
        - `condition`: optional, skip this step if the condition is not met
        - `foreach`: optional, iterate over rows in a context variable; sub_steps run once per row
        - `sub_steps`: required when using `foreach`

        ### ConditionDefinition
        Simple comparison:
        ```json
        {
          "key": "context_variable_name",
          "field": "optional_nested_field",
          "operator": "equals | not_equals | greater_than | less_than | contains | in_range",
          "value": "string",
          "min": "string (for in_range)",
          "max": "string (for in_range)",
          "case_sensitive": false
        }
        ```
        Logical composite (can be nested):
        ```json
        {
          "all_of": [ ConditionDefinition ],
          "any_of": [ ConditionDefinition ],
          "not": ConditionDefinition
        }
        ```

        ## Available Component Types

        ### database_reader
        Executes a SQL query against PostgreSQL. Outputs result rows into the context.
        ```json
        {
          "component_type": "database_reader",
          "component_version": "1.0",
          "description": "...",
          "config": {
            "query": "SELECT col FROM table WHERE x = @param",
            "parameters": { "@param": "{{context_variable}}" },
            "output_key": "db_rows",
            "timeout_seconds": 30
          }
        }
        ```

        ### decision
        Evaluates rules against context variables and sets output variables based on the matching rule.
        ```json
        {
          "component_type": "decision",
          "component_version": "1.0",
          "description": "...",
          "config": {
            "evaluation_mode": "first_match | all_rules",
            "rules": [
              {
                "rule_id": "string",
                "description": "string",
                "condition": ConditionDefinition,
                "outputs": { "output_variable": "value" }
              }
            ],
            "default_outputs": { "output_variable": "default_value" }
          }
        }
        ```

        ### calculate
        Performs mathematical calculations on context variables and stores results.
        ```json
        {
          "component_type": "calculate",
          "component_version": "1.0",
          "description": "...",
          "config": {
            "expressions": [
              { "output": "result_var", "formula": "{{balance}} * {{rate}}" }
            ]
          }
        }
        ```

        ### pdf_generator
        Generates a PDF from a registered template using context variables as data.
        ```json
        {
          "component_type": "pdf_generator",
          "component_version": "1.0",
          "description": "...",
          "config": {
            "template_id": "template_name",
            "template_registry_path": "configs/templates/template-registry.json",
            "output_directory": "output/pdfs/subfolder",
            "filename_pattern": "output_{{context_var}}.pdf"
          }
        }
        ```

        ### email_sender
        Sends an email. Supports template bodies and file attachments.
        ```json
        {
          "component_type": "email_sender",
          "component_version": "1.0",
          "description": "...",
          "config": {
            "from": "sender@example.com",
            "to": ["{{customer_email}}"],
            "subject": "Subject with {{context_var}}",
            "body_template": "{{email_template}}",
            "attachments": ["{{pdf_file_path}}"],
            "reply_to": "support@example.com"
          }
        }
        ```

        ### screen_reader
        Reads data from a terminal/green-screen (legacy mainframe) application screen.

        ### screen_writer
        Writes data to or interacts with a terminal/green-screen (legacy mainframe) application screen.

        ## Context Variable Interpolation
        Values in component configs can reference context variables using `{{variable_name}}` syntax.
        Variables are populated by previous steps. When using foreach with `row_prefix: "loan_"`,
        each field from the row is available as `{{loan_fieldname}}`.
        """;

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

        var context = BuildExplainContext(workflowPath, workflowJson, workflow);

        var request = new ConverseRequest
        {
            ModelId = _modelId,
            System = [new SystemContentBlock { Text = ExplainSystemPrompt }],
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

    public async Task<string> SuggestChangesAsync(string workflowPath, string changeRequest, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Suggesting changes for workflow {WorkflowPath}", workflowPath);

        var workflowJson = await File.ReadAllTextAsync(workflowPath, cancellationToken);
        var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(workflowJson, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse workflow definition.");

        var context = BuildSuggestContext(workflowPath, workflowJson, workflow, changeRequest);

        var request = new ConverseRequest
        {
            ModelId = _modelId,
            System = [new SystemContentBlock { Text = SuggestSystemPrompt }],
            Messages =
            [
                new Message
                {
                    Role = ConversationRole.User,
                    Content = [new ContentBlock { Text = context }]
                }
            ],
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = 4096,
                Temperature = 0.2f
            }
        };

        try
        {
            var response = await _client.ConverseAsync(request, cancellationToken);
            _logger.LogInformation(
                "Suggest succeeded for {WorkflowPath}. Input tokens: {InputTokens}, Output tokens: {OutputTokens}",
                workflowPath, response.Usage?.InputTokens, response.Usage?.OutputTokens);
            return response.Output.Message.Content[0].Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bedrock suggest call failed for workflow {WorkflowPath}", workflowPath);
            throw;
        }
    }

    private static string BuildExplainContext(string workflowPath, string workflowJson, WorkflowDefinition workflow)
    {
        var componentSections = new List<string>();

        CollectStepSections(workflow.Steps, workflowPath, componentSections);

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

    private static string BuildSuggestContext(string workflowPath, string workflowJson, WorkflowDefinition workflow, string changeRequest)
    {
        var componentFiles = new List<(string RelativePath, string Json)>();

        CollectStepFiles(workflow.Steps, workflowPath, componentFiles);

        var sb = new StringBuilder();

        sb.AppendLine("## Change Request");
        sb.AppendLine(changeRequest);
        sb.AppendLine();

        sb.AppendLine("## Current Files");
        sb.AppendLine();

        var workflowRelPath = GetFileRelativePath(workflowPath);
        sb.AppendLine($"### {workflowRelPath}");
        sb.AppendLine($"```json\n{workflowJson}\n```");
        sb.AppendLine();

        foreach (var (relPath, json) in componentFiles)
        {
            sb.AppendLine($"### {relPath}");
            sb.AppendLine($"```json\n{json}\n```");
            sb.AppendLine();
        }

        sb.AppendLine(SchemaAndComponentReference);

        return sb.ToString();
    }

    private static void CollectStepSections(IEnumerable<WorkflowStep> steps, string workflowPath, List<string> sections)
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
                CollectStepSections(step.SubSteps, workflowPath, sections);
        }
    }

    private static void CollectStepFiles(IEnumerable<WorkflowStep> steps, string workflowPath, List<(string, string)> results)
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
                        var relPath = GetFileRelativePath(componentPath);
                        if (!results.Any(r => r.Item1 == relPath))
                        {
                            var json = File.ReadAllText(componentPath);
                            results.Add((relPath, json));
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Skip component configs that resolve outside of allowed workflow config roots.
                }
            }

            if (step.SubSteps is { Count: > 0 })
                CollectStepFiles(step.SubSteps, workflowPath, results);
        }
    }

    private static string GetFileRelativePath(string absolutePath)
    {
        var normalized = absolutePath.Replace('\\', '/');
        var idx = normalized.IndexOf("/configs/", StringComparison.Ordinal);
        return idx >= 0 ? normalized[(idx + 1)..] : Path.GetFileName(absolutePath);
    }

    public void Dispose() => _client.Dispose();
}
