using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hack13.TerminalClient.Workflow;

/// <summary>
/// Top-level configuration for the GreenScreenConnector workflow.
/// </summary>
public class ConnectorConfig
{
    public ConnectionConfig Connection { get; set; } = new();
    public string ScreenCatalogPath { get; set; } = string.Empty;
    public List<WorkflowStep> Steps { get; set; } = new();
}

public class ConnectionConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5250;
    public string TerminalType { get; set; } = "IBM-3179-2";
    public string? DeviceName { get; set; }
    public int ConnectTimeoutSeconds { get; set; } = 5;
    public int ResponseTimeoutSeconds { get; set; } = 10;

    /// <summary>Enable TLS (required for port 992 connections).</summary>
    public bool UseTls { get; set; }

    /// <summary>Path to a CA certificate PEM file to trust (e.g. an internal root CA).</summary>
    public string? CaCertificatePath { get; set; }

    /// <summary>Skip server certificate validation entirely (dev/testing only).</summary>
    public bool InsecureSkipVerify { get; set; }
}

/// <summary>
/// A single step in the connector workflow.
/// </summary>
public class WorkflowStep
{
    public string StepName { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StepType Type { get; set; }

    /// <summary>Fields to fill (navigate steps). Key = field name, value = literal or {{placeholder}}.</summary>
    public Dictionary<string, string>? Fields { get; set; }

    /// <summary>AID key to press (navigate steps). e.g. "Enter", "F6".</summary>
    public string? AidKey { get; set; }

    /// <summary>Expected screen ID after navigation or for assertion.</summary>
    public string? ExpectScreen { get; set; }

    /// <summary>Fields to scrape (scrape steps). List of field names from screen catalog.</summary>
    public List<string>? ScrapeFields { get; set; }

    /// <summary>Screen ID to scrape from (scrape steps).</summary>
    public string? Screen { get; set; }

    /// <summary>Text that should NOT appear on screen (assert steps). If found, step fails.</summary>
    public string? ErrorText { get; set; }

    /// <summary>Row to check for error text.</summary>
    public int? ErrorRow { get; set; }

    /// <summary>
    /// Field assertions. Key = field name from screen catalog, value = expected value
    /// (literal or {{placeholder}}). Supports assert_operator to change comparison style.
    /// </summary>
    public Dictionary<string, string>? AssertFields { get; set; }

    /// <summary>
    /// Comparison operator for assert_fields: equals, not_equals, contains, starts_with, ends_with.
    /// Defaults to equals.
    /// </summary>
    public string? AssertOperator { get; set; }

    /// <summary>Max retries for this step (default 1 = no retry).</summary>
    public int MaxRetries { get; set; } = 1;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepType
{
    [JsonPropertyName("navigate")]
    Navigate,
    [JsonPropertyName("scrape")]
    Scrape,
    [JsonPropertyName("assert")]
    Assert
}
