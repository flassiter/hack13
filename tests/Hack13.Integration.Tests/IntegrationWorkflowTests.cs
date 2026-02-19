using Hack13.Contracts.Enums;
using Hack13.EmailSender;
using Hack13.Orchestrator;

namespace Hack13.Integration.Tests;

public sealed class IntegrationWorkflowTests : IDisposable
{
    private readonly string _tempDir;

    public IntegrationWorkflowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rpa-integration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ExecuteAsync_CalculationDecisionEmailFlow_SucceedsEndToEnd()
    {
        var attachmentPath = Path.Combine(_tempDir, "statement.pdf");
        await File.WriteAllBytesAsync(attachmentPath, Enumerable.Repeat((byte)'A', 2048).ToArray());

        WriteJson("calc.json", """
            {
              "component_type": "calculate",
              "component_version": "1.0",
              "config": {
                "calculations": [
                  {
                    "name": "shortage",
                    "output_key": "escrow_shortage_surplus",
                    "operation": "subtract",
                    "inputs": ["escrow_balance", "required_reserve"],
                    "format": { "decimal_places": 2 }
                  }
                ]
              }
            }
            """);

        WriteJson("decision.json", """
            {
              "component_type": "decision",
              "component_version": "1.0",
              "config": {
                "evaluation_mode": "first_match",
                "rules": [
                  {
                    "rule_name": "shortage",
                    "condition": { "field": "escrow_shortage_surplus", "operator": "less_than", "value": "0" },
                    "outputs": { "notice_type": "shortage" }
                  }
                ]
              }
            }
            """);

        WriteJson("email.json", """
            {
              "component_type": "email_sender",
              "component_version": "1.0",
              "config": {
                "from": "statements@example.com",
                "to": ["{{customer_email}}"],
                "subject": "Loan {{loan_number}} - {{notice_type}}",
                "body": "<p>Escrow notice for {{loan_number}}</p>",
                "attachments": ["{{pdf_file_path}}"],
                "reply_to": "support@example.com"
              }
            }
            """);

        var workflowPath = WriteJson("workflow.json", """
            {
              "workflow_id": "integration_flow",
              "workflow_version": "1.0",
              "initial_parameters": ["loan_number", "customer_email", "pdf_file_path", "escrow_balance", "required_reserve"],
              "steps": [
                { "step_name": "calculate", "component_type": "calculate", "component_config": "./calc.json" },
                { "step_name": "decide", "component_type": "decision", "component_config": "./decision.json" },
                { "step_name": "email", "component_type": "email_sender", "component_config": "./email.json" }
              ]
            }
            """);

        var orchestrator = new WorkflowOrchestrator(
            registry: ComponentRegistry.CreateDefault(new EmailSenderEnvironmentConfig
            {
                TemplateBasePath = _tempDir,
                Transport = new TransportConfig { Type = "mock" }
            }));

        var summary = await orchestrator.ExecuteAsync(workflowPath, new Dictionary<string, string>
        {
            ["loan_number"] = "1000001",
            ["customer_email"] = "customer@example.com",
            ["pdf_file_path"] = attachmentPath,
            ["escrow_balance"] = "$2,150.00",
            ["required_reserve"] = "$2,800.00"
        });

        Assert.Equal(ComponentStatus.Success, summary.FinalStatus);
        Assert.Equal(3, summary.Steps.Count);
        Assert.Equal("-650.00", summary.FinalDataDictionary["escrow_shortage_surplus"]);
        Assert.Equal("shortage", summary.FinalDataDictionary["notice_type"]);
        Assert.Equal("sent", summary.FinalDataDictionary["email_status"]);
        Assert.False(string.IsNullOrWhiteSpace(summary.FinalDataDictionary["email_message_id"]));
    }

    [Fact]
    public async Task ExecuteAsync_LogAndContinueEmailFailure_AllowsFollowingStepToRun()
    {
        WriteJson("email.json", """
            {
              "component_type": "email_sender",
              "component_version": "1.0",
              "config": {
                "from": "statements@example.com",
                "to": ["{{customer_email}}"],
                "subject": "Loan {{loan_number}}",
                "body": "<p>Escrow notice</p>",
                "attachments": ["{{pdf_file_path}}"],
                "reply_to": "support@example.com"
              }
            }
            """);

        WriteJson("calc.json", """
            {
              "component_type": "calculate",
              "component_version": "1.0",
              "config": {
                "calculations": [
                  {
                    "name": "post_email_calc",
                    "output_key": "post_email_marker",
                    "operation": "add",
                    "inputs": ["1", "1"]
                  }
                ]
              }
            }
            """);

        var workflowPath = WriteJson("workflow.json", """
            {
              "workflow_id": "integration_log_continue",
              "workflow_version": "1.0",
              "initial_parameters": ["loan_number", "customer_email", "pdf_file_path"],
              "steps": [
                {
                  "step_name": "email",
                  "component_type": "email_sender",
                  "component_config": "./email.json",
                  "on_failure": "log_and_continue"
                },
                {
                  "step_name": "calculate_after_email",
                  "component_type": "calculate",
                  "component_config": "./calc.json"
                }
              ]
            }
            """);

        var orchestrator = new WorkflowOrchestrator(
            registry: ComponentRegistry.CreateDefault(new EmailSenderEnvironmentConfig
            {
                TemplateBasePath = _tempDir,
                Transport = new TransportConfig { Type = "mock" }
            }));

        var summary = await orchestrator.ExecuteAsync(workflowPath, new Dictionary<string, string>
        {
            ["loan_number"] = "1000001",
            ["customer_email"] = "customer@example.com",
            ["pdf_file_path"] = Path.Combine(_tempDir, "missing.pdf")
        });

        Assert.Equal(ComponentStatus.Success, summary.FinalStatus);
        Assert.Equal(ComponentStatus.Failure, summary.Steps[0].Status);
        Assert.Equal(ComponentStatus.Success, summary.Steps[1].Status);
        Assert.Equal("2", summary.FinalDataDictionary["post_email_marker"]);
    }

    private string WriteJson(string name, string json)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, json);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
