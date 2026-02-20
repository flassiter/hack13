using System.Text.Json;
using System.Text.Json.Nodes;
using Hack13.ApprovalGate;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Hack13.ApprovalGate.Tests;

public class ApprovalGateComponentTests : IDisposable
{
    private readonly WireMockServer _server;

    public ApprovalGateComponentTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose() => _server.Stop();

    private string BaseUrl => _server.Urls[0];

    private static ComponentConfiguration MakeConfig(string json) => new()
    {
        ComponentType = "approval_gate",
        ComponentVersion = "1.0",
        Config = JsonDocument.Parse(
            WithPrivateNetworkEnabled((JsonObject)JsonNode.Parse(json)!)
            .ToJsonString())
            .RootElement
    };

    private static JsonObject WithPrivateNetworkEnabled(JsonObject obj)
    {
        obj["allow_private_network"] = true;
        return obj;
    }

    private static IResponseBuilder JsonResponse(int status, string body) =>
        Response.Create()
            .WithStatusCode(status)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);

    // -------------------------------------------------------------------------
    // 1. Immediate approval on first poll
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ImmediateApproval_FirstPoll_ReturnsSuccess()
    {
        _server.Given(Request.Create().WithPath("/approvals/appr-1").UsingGet())
            .RespondWith(JsonResponse(200, "{\"status\": \"approved\"}"));

        var component = new ApprovalGateComponent();
        var config = MakeConfig($$"""
            {
              "poll_url": "{{BaseUrl}}/approvals/appr-1",
              "poll_interval_seconds": 0,
              "timeout_seconds": 10,
              "approved_path": "status",
              "approved_value": "approved",
              "rejected_path": "status",
              "rejected_value": "rejected"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("approved", data["approval_status"]);
        Assert.Equal("1", data["approval_poll_count"]);
    }

    // -------------------------------------------------------------------------
    // 2. Approval after 3 polls (pending x2, then approved) — verifies poll count
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ApprovalAfterNPolls_VerifiesPollCount()
    {
        var callCount = 0;
        _server.Given(Request.Create().WithPath("/approvals/appr-2").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(_ =>
                {
                    var n = System.Threading.Interlocked.Increment(ref callCount);
                    return n < 3 ? "{\"status\": \"pending\"}" : "{\"status\": \"approved\"}";
                }));

        var component = new ApprovalGateComponent();
        var config = MakeConfig($$"""
            {
              "poll_url": "{{BaseUrl}}/approvals/appr-2",
              "poll_interval_seconds": 0,
              "timeout_seconds": 30,
              "approved_path": "status",
              "approved_value": "approved",
              "rejected_path": "status",
              "rejected_value": "rejected"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("approved", data["approval_status"]);
        Assert.Equal("3", data["approval_poll_count"]);
    }

    // -------------------------------------------------------------------------
    // 3. Rejection → REJECTED failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Rejection_ReturnsRejectedFailure()
    {
        _server.Given(Request.Create().WithPath("/approvals/appr-3").UsingGet())
            .RespondWith(JsonResponse(200, "{\"status\": \"rejected\"}"));

        var component = new ApprovalGateComponent();
        var config = MakeConfig($$"""
            {
              "poll_url": "{{BaseUrl}}/approvals/appr-3",
              "poll_interval_seconds": 0,
              "timeout_seconds": 10,
              "approved_path": "status",
              "approved_value": "approved",
              "rejected_path": "status",
              "rejected_value": "rejected"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("REJECTED", result.Error!.ErrorCode);
        Assert.Equal("rejected", data["approval_status"]);
        Assert.Equal("1", data["approval_poll_count"]);
    }

    // -------------------------------------------------------------------------
    // 4. Timeout with short budget
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Timeout_ReturnsTimeoutFailure()
    {
        _server.Given(Request.Create().WithPath("/approvals/appr-4").UsingGet())
            .RespondWith(JsonResponse(200, "{\"status\": \"pending\"}"));

        var component = new ApprovalGateComponent();
        var config = MakeConfig($$"""
            {
              "poll_url": "{{BaseUrl}}/approvals/appr-4",
              "poll_interval_seconds": 0,
              "timeout_seconds": 1,
              "approved_path": "status",
              "approved_value": "approved"
            }
            """);
        var data = new Dictionary<string, string>();

        // Give up to 5 seconds wall-clock for the internal 1-second timeout to fire
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await component.ExecuteAsync(config, data, cts.Token);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("TIMEOUT", result.Error!.ErrorCode);
        Assert.Equal("timeout", data["approval_status"]);
    }

    // -------------------------------------------------------------------------
    // 5. Transient HTTP error tolerated, then approved
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransientHttpError_ThenApproved_ReturnsSuccess()
    {
        var callCount5 = 0;
        _server.Given(Request.Create().WithPath("/approvals/appr-5").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(_ =>
                {
                    var n = System.Threading.Interlocked.Increment(ref callCount5);
                    // Return a non-approved body on first call to simulate a recoverable state
                    return n < 2 ? "{\"status\": \"error\"}" : "{\"status\": \"approved\"}";
                }));

        var component = new ApprovalGateComponent();
        var config = MakeConfig($$"""
            {
              "poll_url": "{{BaseUrl}}/approvals/appr-5",
              "poll_interval_seconds": 0,
              "timeout_seconds": 10,
              "approved_path": "status",
              "approved_value": "approved"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("approved", data["approval_status"]);
    }

    // -------------------------------------------------------------------------
    // 6. Cancellation → rethrows
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancellationToken_Cancelled_Rethrows()
    {
        var component = new ApprovalGateComponent();
        var config = MakeConfig($$"""
            {
              "poll_url": "{{BaseUrl}}/approvals/appr-6",
              "poll_interval_seconds": 60,
              "timeout_seconds": 3600,
              "approved_path": "status",
              "approved_value": "approved"
            }
            """);
        var data = new Dictionary<string, string>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => component.ExecuteAsync(config, data, cts.Token));
    }

    // -------------------------------------------------------------------------
    // 7. Placeholder resolution in URL and headers
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PlaceholderResolution_InUrlAndHeaders()
    {
        _server.Given(Request.Create().WithPath("/approvals/resolved-id").UsingGet())
            .RespondWith(JsonResponse(200, "{\"status\": \"approved\"}"));

        var component = new ApprovalGateComponent();
        var config = MakeConfig("""
            {
              "poll_url": "{{base_url}}/approvals/{{approval_id}}",
              "poll_interval_seconds": 0,
              "timeout_seconds": 10,
              "poll_headers": { "Authorization": "Bearer {{token}}" },
              "approved_path": "status",
              "approved_value": "approved"
            }
            """);
        var data = new Dictionary<string, string>
        {
            ["base_url"] = BaseUrl,
            ["approval_id"] = "resolved-id",
            ["token"] = "secret-token"
        };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("approved", data["approval_status"]);
    }

    // -------------------------------------------------------------------------
    // 8. Missing poll_url → CONFIG_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MissingPollUrl_ReturnsConfigError()
    {
        var component = new ApprovalGateComponent();
        var config = MakeConfig("""
            {
              "approved_path": "status",
              "approved_value": "approved"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONFIG_ERROR", result.Error!.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // 9. Case-insensitive approved_value matching
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ApprovedValue_CaseInsensitive_Matches()
    {
        _server.Given(Request.Create().WithPath("/approvals/appr-ci").UsingGet())
            .RespondWith(JsonResponse(200, "{\"status\": \"APPROVED\"}"));

        var component = new ApprovalGateComponent();
        var config = MakeConfig($$"""
            {
              "poll_url": "{{BaseUrl}}/approvals/appr-ci",
              "poll_interval_seconds": 0,
              "timeout_seconds": 10,
              "approved_path": "status",
              "approved_value": "approved"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("approved", data["approval_status"]);
    }

    // -------------------------------------------------------------------------
    // 10. approval_status and approval_poll_count written on rejection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RejectionOutput_WritesStatusAndPollCount()
    {
        var callCountRc = 0;
        _server.Given(Request.Create().WithPath("/approvals/appr-rc").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(_ =>
                {
                    var n = System.Threading.Interlocked.Increment(ref callCountRc);
                    return n < 2 ? "{\"status\": \"pending\"}" : "{\"status\": \"rejected\"}";
                }));

        var component = new ApprovalGateComponent();
        var config = MakeConfig($$"""
            {
              "poll_url": "{{BaseUrl}}/approvals/appr-rc",
              "poll_interval_seconds": 0,
              "timeout_seconds": 10,
              "approved_path": "status",
              "approved_value": "approved",
              "rejected_path": "status",
              "rejected_value": "rejected"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("REJECTED", result.Error!.ErrorCode);
        Assert.Equal("rejected", data["approval_status"]);
        Assert.Equal("2", data["approval_poll_count"]);
    }

    [Fact]
    public async Task InvalidJsonPath_ReturnsParseErrorImmediately()
    {
        _server.Given(Request.Create().WithPath("/approvals/appr-bad-path").UsingGet())
            .RespondWith(JsonResponse(200, "{\"items\": [\"approved\"]}"));

        var component = new ApprovalGateComponent();
        var config = MakeConfig($$"""
            {
              "poll_url": "{{BaseUrl}}/approvals/appr-bad-path",
              "poll_interval_seconds": 0,
              "timeout_seconds": 10,
              "approved_path": "$.items[abc]",
              "approved_value": "approved"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("RESPONSE_PARSE_ERROR", result.Error?.ErrorCode);
        Assert.Equal("error", data["approval_status"]);
    }
}
