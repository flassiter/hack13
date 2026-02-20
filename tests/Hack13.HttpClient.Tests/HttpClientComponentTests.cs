using System.Text.Json;
using System.Text.Json.Nodes;
using Hack13.HttpClient;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Hack13.HttpClient.Tests;

public class HttpClientComponentTests : IDisposable
{
    private readonly WireMockServer _server;

    public HttpClientComponentTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose() => _server.Stop();

    private string BaseUrl => _server.Urls[0];

    private static ComponentConfiguration MakeConfig(string json) => new()
    {
        ComponentType = "http_client",
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

    // -------------------------------------------------------------------------
    // 1. GET success — status written, body written to default key
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_Success_WritesStatusAndBody()
    {
        _server.Given(Request.Create().WithPath("/api/test").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\": \"ok\"}"));

        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "GET",
              "url": "{{BaseUrl}}/api/test"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("200", data["http_status_code"]);
        Assert.Contains("http_response_body", data.Keys);
        Assert.Contains("\"message\"", data["http_response_body"]);
    }

    // -------------------------------------------------------------------------
    // 2. POST with body and custom headers
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Post_WithBodyAndHeaders_SendsCorrectly()
    {
        _server.Given(Request.Create().WithPath("/api/letters").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"id\": \"letter-123\", \"status\": \"queued\"}"));

        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "POST",
              "url": "{{BaseUrl}}/api/letters",
              "headers": { "Authorization": "Bearer token123" },
              "body": "{\"loanNumber\": \"LN001\"}"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("201", data["http_status_code"]);
    }

    // -------------------------------------------------------------------------
    // 3. Placeholder resolution in URL, body, and headers
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PlaceholderResolution_InUrlBodyAndHeaders()
    {
        _server.Given(Request.Create().WithPath("/api/items/42").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"found\": true}"));

        var component = new HttpClientComponent();
        var config = MakeConfig("""
            {
              "method": "GET",
              "url": "{{base_url}}/api/items/{{item_id}}",
              "headers": { "X-Token": "{{token}}" }
            }
            """);
        var data = new Dictionary<string, string>
        {
            ["base_url"] = BaseUrl,
            ["item_id"] = "42",
            ["token"] = "secret"
        };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("200", data["http_status_code"]);
    }

    // -------------------------------------------------------------------------
    // 4. Response field mapping — flat field
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResponseFieldMap_FlatField_WritesToDataDictionary()
    {
        _server.Given(Request.Create().WithPath("/api/approval").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"id\": \"appr-999\", \"status\": \"pending\"}"));

        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "POST",
              "url": "{{BaseUrl}}/api/approval",
              "response_field_map": {
                "approval_id": "$.id",
                "queue_status": "$.status"
              }
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("appr-999", data["approval_id"]);
        Assert.Equal("pending", data["queue_status"]);
    }

    // -------------------------------------------------------------------------
    // 5. Response field mapping — nested field
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResponseFieldMap_NestedField_ExtractsCorrectly()
    {
        _server.Given(Request.Create().WithPath("/api/nested").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"data\": {\"user\": {\"name\": \"Alice\"}}}"));

        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "GET",
              "url": "{{BaseUrl}}/api/nested",
              "response_field_map": {
                "user_name": "$.data.user.name"
              }
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("Alice", data["user_name"]);
    }

    // -------------------------------------------------------------------------
    // 6. Non-2xx treated as HTTP_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Non2xx_ReturnsHttpError()
    {
        _server.Given(Request.Create().WithPath("/api/fail").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "GET",
              "url": "{{BaseUrl}}/api/fail"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("HTTP_ERROR", result.Error!.ErrorCode);
        Assert.Equal("404", data["http_status_code"]);
    }

    // -------------------------------------------------------------------------
    // 7. Custom success_status_codes — 202 accepted, 404 accepted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CustomSuccessStatusCodes_Accepted()
    {
        _server.Given(Request.Create().WithPath("/api/accepted").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(202).WithBody("{}"));

        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "GET",
              "url": "{{BaseUrl}}/api/accepted",
              "success_status_codes": [200, 202, 204]
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("202", data["http_status_code"]);
    }

    // -------------------------------------------------------------------------
    // 8. response_body_key written
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResponseBodyKey_WritesBodyUnderCustomKey()
    {
        _server.Given(Request.Create().WithPath("/api/body").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"x\": 1}"));

        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "GET",
              "url": "{{BaseUrl}}/api/body",
              "response_body_key": "raw_body"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Contains("raw_body", data.Keys);
        Assert.DoesNotContain("http_response_body", data.Keys);
    }

    // -------------------------------------------------------------------------
    // 9. http_status_code always written, even on failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HttpStatusCode_AlwaysWritten_OnFailure()
    {
        _server.Given(Request.Create().WithPath("/api/server-error").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("error"));

        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "GET",
              "url": "{{BaseUrl}}/api/server-error"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("500", data["http_status_code"]);
    }

    // -------------------------------------------------------------------------
    // 10. Missing URL → CONFIG_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MissingUrl_ReturnsConfigError()
    {
        var component = new HttpClientComponent();
        var config = MakeConfig("""{ "method": "GET" }""");
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONFIG_ERROR", result.Error!.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // 11. Invalid HTTP method → CONFIG_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvalidMethod_ReturnsConfigError()
    {
        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "SEND",
              "url": "{{BaseUrl}}/api/test"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONFIG_ERROR", result.Error!.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // 12. Cancellation → rethrows
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancellationToken_Cancelled_Rethrows()
    {
        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "GET",
              "url": "{{BaseUrl}}/api/test"
            }
            """);
        var data = new Dictionary<string, string>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => component.ExecuteAsync(config, data, cts.Token));
    }

    // -------------------------------------------------------------------------
    // 13. ExtractJsonValue — array index access
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResponseFieldMap_ArrayIndex_ExtractsCorrectly()
    {
        _server.Given(Request.Create().WithPath("/api/array").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"items\": [\"first\", \"second\", \"third\"]}"));

        var component = new HttpClientComponent();
        var config = MakeConfig($$"""
            {
              "method": "GET",
              "url": "{{BaseUrl}}/api/array",
              "response_field_map": {
                "first_item": "$.items[0]",
                "second_item": "$.items[1]"
              }
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("first", data["first_item"]);
        Assert.Equal("second", data["second_item"]);
    }

    [Fact]
    public async Task PrivateNetworkBlocked_WhenAllowPrivateNetworkIsFalse()
    {
        var component = new HttpClientComponent();
        var config = new ComponentConfiguration
        {
            ComponentType = "http_client",
            ComponentVersion = "1.0",
            Config = JsonDocument.Parse($$"""
                {
                  "method": "GET",
                  "url": "{{BaseUrl}}/api/test",
                  "allow_private_network": false
                }
                """).RootElement
        };
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONFIG_ERROR", result.Error?.ErrorCode);
    }
}
