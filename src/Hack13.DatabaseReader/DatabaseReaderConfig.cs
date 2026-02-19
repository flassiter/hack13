namespace Hack13.DatabaseReader;

internal class DatabaseReaderConfig
{
    public string Provider { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public Dictionary<string, string>? Parameters { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;
    public string? OutputPrefix { get; set; }
    public bool RequireRow { get; set; } = false;
}
