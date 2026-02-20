namespace Hack13.DatabaseWriter;

internal sealed class DatabaseWriterConfig
{
    public string Provider { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public Dictionary<string, string>? Parameters { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;
    public string OutputKey { get; set; } = "rows_affected";
    public bool Scalar { get; set; } = false;
}
