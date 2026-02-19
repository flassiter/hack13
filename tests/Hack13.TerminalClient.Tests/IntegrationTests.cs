using System.Text.Json;
using Microsoft.Extensions.Logging;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Models;
using Hack13.TerminalServer.Engine;
using Hack13.TerminalServer.Navigation;
using Hack13.TerminalServer.Server;

namespace Hack13.TerminalClient.Tests;

/// <summary>
/// Integration tests that start the mock TN5250 server and run the GreenScreenConnector against it.
/// </summary>
public class IntegrationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly string _configsPath;
    private readonly string _testDataPath;

    public IntegrationTests()
    {
        // Find the configs directory by walking up from the test output directory
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "configs")))
            dir = Path.GetDirectoryName(dir);

        _configsPath = dir != null ? Path.Combine(dir, "configs") : throw new DirectoryNotFoundException("Could not find configs directory");
        _testDataPath = dir != null ? Path.Combine(dir, "test-data") : throw new DirectoryNotFoundException("Could not find test-data directory");
    }

    public void Dispose() { }

    private (Tn5250Server server, int port) CreateMockServer()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
        var screenLoader = new ScreenLoader();
        screenLoader.LoadFromDirectory(Path.Combine(_configsPath, "screen-catalog"));
        var navConfig = NavigationConfig.LoadFromFile(Path.Combine(_configsPath, "navigation.json"));
        var testData = new TestDataStore();
        testData.LoadFromFile(Path.Combine(_testDataPath, "loans.json"));

        // Use port 0 to get a random available port
        var port = GetAvailablePort();
        var server = new Tn5250Server(port, screenLoader, navConfig, testData, loggerFactory);
        return (server, port);
    }

    private static int GetAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task<ComponentResult> RunWorkflowAsync(
        int port,
        string loanNumber,
        string userId = "TESTUSER",
        string password = "TEST1234",
        string? workflowJson = null,
        string? screenCatalogPath = null)
    {
        workflowJson ??= File.ReadAllText(Path.Combine(_configsPath, "workflows", "escrow-lookup.json"));
        screenCatalogPath ??= Path.Combine(_configsPath, "screen-catalog");

        var config = new ComponentConfiguration
        {
            ComponentType = "green_screen_connector",
            ComponentVersion = "1.0.0",
            Description = "Escrow lookup test",
            Config = JsonDocument.Parse(workflowJson).RootElement
        };

        var dataDictionary = new Dictionary<string, string>
        {
            ["host"] = "127.0.0.1",
            ["port"] = port.ToString(),
            ["user_id"] = userId,
            ["password"] = password,
            ["loan_number"] = loanNumber,
            ["screen_catalog_path"] = screenCatalogPath
        };

        var connector = new GreenScreenConnector();
        return await connector.ExecuteAsync(config, dataDictionary);
    }

    [Fact]
    public async Task Full_escrow_lookup_returns_all_fields_for_loan_1000001()
    {
        var (server, port) = CreateMockServer();
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        try
        {
            await Task.Delay(200); // Let server start

            var result = await RunWorkflowAsync(port, "1000001");

            Assert.Equal(ComponentStatus.Success, result.Status);
            Assert.Null(result.Error);
            Assert.True(result.DurationMs > 0);

            // Verify loan detail fields (10)
            Assert.Equal("SMITH, JOHN A", result.OutputData["borrower_name"]);
            Assert.Equal("123 OAK STREET, ANYTOWN TX 75001", result.OutputData["property_address"]);
            Assert.Equal("Conventional", result.OutputData["loan_type"]);
            Assert.Equal("$250,000.00", result.OutputData["original_amount"]);
            Assert.Equal("$198,543.21", result.OutputData["current_balance"]);
            Assert.Equal("4.250%", result.OutputData["interest_rate"]);
            Assert.Equal("$1,229.85", result.OutputData["monthly_payment"]);
            Assert.Equal("03/01/2025", result.OutputData["next_due_date"]);
            Assert.Equal("Current", result.OutputData["loan_status"]);
            Assert.Equal("06/15/2020", result.OutputData["origination_date"]);

            // Verify escrow fields (13)
            Assert.Equal("$2,150.00", result.OutputData["escrow_balance"]);
            Assert.Equal("$485.00", result.OutputData["escrow_payment"]);
            Assert.Equal("$2,800.00", result.OutputData["required_reserve"]);
            Assert.Equal("$650.00", result.OutputData["shortage_amount"]);
            Assert.Equal("$0.00", result.OutputData["surplus_amount"]);
            Assert.Equal("Shortage", result.OutputData["escrow_status"]);
            Assert.Equal("$3,200.00", result.OutputData["tax_amount"]);
            Assert.Equal("$1,450.00", result.OutputData["hazard_insurance"]);
            Assert.Equal("$620.00", result.OutputData["flood_insurance"]);
            Assert.Equal("$0.00", result.OutputData["mortgage_insurance"]);
            Assert.Equal("01/15/2025", result.OutputData["last_analysis_date"]);
            Assert.Equal("01/15/2026", result.OutputData["next_analysis_date"]);
            Assert.Equal("$1,800.00", result.OutputData["projected_balance"]);

            // Total: 23 fields
            Assert.Equal(23, result.OutputData.Count);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Theory]
    [InlineData("1000002", "JOHNSON, MARIA L", "FHA", "Surplus")]
    [InlineData("1000003", "WILLIAMS, ROBERT T", "VA", "Shortage")]
    public async Task Full_escrow_lookup_returns_correct_data_for_each_test_loan(
        string loanNumber, string expectedBorrower, string expectedLoanType, string expectedEscrowStatus)
    {
        var (server, port) = CreateMockServer();
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        try
        {
            await Task.Delay(200);

            var result = await RunWorkflowAsync(port, loanNumber);

            Assert.Equal(ComponentStatus.Success, result.Status);
            Assert.Equal(expectedBorrower, result.OutputData["borrower_name"]);
            Assert.Equal(expectedLoanType, result.OutputData["loan_type"]);
            Assert.Equal(expectedEscrowStatus, result.OutputData["escrow_status"]);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task Invalid_loan_number_returns_failure()
    {
        var (server, port) = CreateMockServer();
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        try
        {
            await Task.Delay(200);

            var result = await RunWorkflowAsync(port, "9999999");

            Assert.Equal(ComponentStatus.Failure, result.Status);
            Assert.NotNull(result.Error);
            Assert.Contains("STEP_FAILED", result.Error.ErrorCode);
            Assert.True(result.LogEntries.Count > 0);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task Invalid_credentials_returns_failure()
    {
        var (server, port) = CreateMockServer();
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        try
        {
            await Task.Delay(200);

            var result = await RunWorkflowAsync(port, "1000001", "BADUSER", "BADPASS");

            Assert.Equal(ComponentStatus.Failure, result.Status);
            Assert.NotNull(result.Error);
            Assert.True(result.LogEntries.Count > 0);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task Two_concurrent_clients_succeed()
    {
        var (server, port) = CreateMockServer();
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        try
        {
            await Task.Delay(200);

            var task1 = RunWorkflowAsync(port, "1000001");
            var task2 = RunWorkflowAsync(port, "1000002");

            var results = await Task.WhenAll(task1, task2);

            Assert.Equal(ComponentStatus.Success, results[0].Status);
            Assert.Equal(ComponentStatus.Success, results[1].Status);
            Assert.Equal("SMITH, JOHN A", results[0].OutputData["borrower_name"]);
            Assert.Equal("JOHNSON, MARIA L", results[1].OutputData["borrower_name"]);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task Workflow_produces_diagnostic_log_entries()
    {
        var (server, port) = CreateMockServer();
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        try
        {
            await Task.Delay(200);

            var result = await RunWorkflowAsync(port, "1000001");

            Assert.Equal(ComponentStatus.Success, result.Status);
            Assert.True(result.LogEntries.Count > 5, "Expected multiple log entries");
            Assert.All(result.LogEntries, e => Assert.Equal("green_screen_connector", e.ComponentType));

            // Should have Info-level entries for navigation steps
            var infoEntries = result.LogEntries.Where(e => e.Level == Hack13.Contracts.Enums.LogLevel.Info).ToList();
            Assert.True(infoEntries.Count >= 4, "Expected at least 4 info log entries");
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task Workflow_logs_do_not_include_plaintext_passwords()
    {
        var (server, port) = CreateMockServer();
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        try
        {
            await Task.Delay(200);
            var result = await RunWorkflowAsync(port, "1000001");

            Assert.Equal(ComponentStatus.Success, result.Status);
            Assert.DoesNotContain(result.LogEntries, e => e.Message.Contains("TEST1234", StringComparison.Ordinal));
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task Scrape_step_fails_when_catalog_field_is_missing()
    {
        var (server, port) = CreateMockServer();
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        try
        {
            await Task.Delay(200);

            var workflowPath = Path.Combine(_configsPath, "workflows", "escrow-lookup.json");
            var workflow = JsonDocument.Parse(File.ReadAllText(workflowPath)).RootElement;
            using var writableDoc = JsonDocument.Parse(workflow.GetRawText());
            var json = writableDoc.RootElement.GetRawText();
            json = json.Replace("\"projected_balance\"",
                "\"projected_balance\", \"not_a_real_field\"", StringComparison.Ordinal);

            var result = await RunWorkflowAsync(port, "1000001", workflowJson: json);
            Assert.Equal(ComponentStatus.Failure, result.Status);
            Assert.NotNull(result.Error);
            Assert.Equal("STEP_FAILED", result.Error.ErrorCode);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task Duplicate_screen_ids_in_catalog_return_config_failure()
    {
        var catalogDir = Path.Combine(Path.GetTempPath(), $"rpa-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(catalogDir);
        try
        {
            var screenA = """
            {
              "screen_id": "dup",
              "identifier": { "row": 1, "col": 1, "expected_text": "A" },
              "fields": []
            }
            """;
            var screenB = """
            {
              "screen_id": "dup",
              "identifier": { "row": 1, "col": 1, "expected_text": "B" },
              "fields": []
            }
            """;
            File.WriteAllText(Path.Combine(catalogDir, "a.json"), screenA);
            File.WriteAllText(Path.Combine(catalogDir, "b.json"), screenB);

            var workflowJson = File.ReadAllText(Path.Combine(_configsPath, "workflows", "escrow-lookup.json"));
            var result = await RunWorkflowAsync(5250, "1000001", workflowJson: workflowJson, screenCatalogPath: catalogDir);

            Assert.Equal(ComponentStatus.Failure, result.Status);
            Assert.NotNull(result.Error);
            Assert.Equal("UNEXPECTED_ERROR", result.Error.ErrorCode);
            Assert.Contains("Duplicate screen_id", result.Error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(catalogDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Assert_step_can_validate_screen_fields_with_equals_operator()
    {
        var (server, port) = CreateMockServer();
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        try
        {
            await Task.Delay(200);

            var workflowJson = """
            {
              "connection": {
                "host": "{{host}}",
                "port": 5250,
                "connect_timeout_seconds": 5,
                "response_timeout_seconds": 10
              },
              "screen_catalog_path": "{{screen_catalog_path}}",
              "steps": [
                {
                  "step_name": "sign_on",
                  "type": "Navigate",
                  "fields": { "user_id": "{{user_id}}", "password": "{{password}}" },
                  "aid_key": "Enter",
                  "expect_screen": "loan_inquiry"
                },
                {
                  "step_name": "enter_loan_number",
                  "type": "Navigate",
                  "fields": { "loan_number": "{{loan_number}}" },
                  "aid_key": "Enter",
                  "expect_screen": "loan_details"
                },
                {
                  "step_name": "scrape_loan_details",
                  "type": "Scrape",
                  "screen": "loan_details",
                  "scrape_fields": [ "borrower_name" ]
                },
                {
                  "step_name": "assert_borrower_name",
                  "type": "Assert",
                  "screen": "loan_details",
                  "assert_operator": "equals",
                  "assert_fields": {
                    "borrower_name": "SMITH, JOHN A"
                  }
                }
              ]
            }
            """;

            var result = await RunWorkflowAsync(port, "1000001", workflowJson: workflowJson);
            Assert.Equal(ComponentStatus.Success, result.Status);
            Assert.Equal("SMITH, JOHN A", result.OutputData["borrower_name"]);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task Assert_step_fails_when_field_value_does_not_match()
    {
        var (server, port) = CreateMockServer();
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        try
        {
            await Task.Delay(200);

            var workflowJson = """
            {
              "connection": {
                "host": "{{host}}",
                "port": 5250,
                "connect_timeout_seconds": 5,
                "response_timeout_seconds": 10
              },
              "screen_catalog_path": "{{screen_catalog_path}}",
              "steps": [
                {
                  "step_name": "sign_on",
                  "type": "Navigate",
                  "fields": { "user_id": "{{user_id}}", "password": "{{password}}" },
                  "aid_key": "Enter",
                  "expect_screen": "loan_inquiry"
                },
                {
                  "step_name": "enter_loan_number",
                  "type": "Navigate",
                  "fields": { "loan_number": "{{loan_number}}" },
                  "aid_key": "Enter",
                  "expect_screen": "loan_details"
                },
                {
                  "step_name": "assert_wrong_name",
                  "type": "Assert",
                  "screen": "loan_details",
                  "assert_operator": "equals",
                  "assert_fields": {
                    "borrower_name": "NOT A REAL BORROWER"
                  }
                }
              ]
            }
            """;

            var result = await RunWorkflowAsync(port, "1000001", workflowJson: workflowJson);
            Assert.Equal(ComponentStatus.Failure, result.Status);
            Assert.NotNull(result.Error);
            Assert.Equal("STEP_FAILED", result.Error.ErrorCode);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch { }
        }
    }
}
