using System.Text.Json;
using Hack13.DatabaseWriter;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Models;
using Microsoft.Data.Sqlite;

namespace Hack13.DatabaseWriter.Tests;

public class DatabaseWriterComponentTests : IDisposable
{
    private readonly string _dbName;
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAlive;

    public DatabaseWriterComponentTests()
    {
        _dbName = $"hack13_writer_test_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={_dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    public void Dispose() => _keepAlive.Dispose();

    private void Execute(string sql)
    {
        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private T? Query<T>(string sql)
    {
        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = sql;
        var result = cmd.ExecuteScalar();
        if (result is null or DBNull) return default;
        return (T)Convert.ChangeType(result, typeof(T));
    }

    private static ComponentConfiguration MakeConfig(string json) => new()
    {
        ComponentType = "database_writer",
        ComponentVersion = "1.0",
        Config = JsonDocument.Parse(json).RootElement
    };

    // -------------------------------------------------------------------------
    // 1. Happy path: INSERT a row, verify rows_affected = 1
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HappyPath_Insert_ReturnsRowsAffected()
    {
        Execute("CREATE TABLE IF NOT EXISTS loans (loan_number TEXT, bucket_id TEXT)");

        var component = new DatabaseWriterComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "INSERT INTO loans (loan_number, bucket_id) VALUES (@loan_number, @bucket_id)",
              "parameters": { "loan_number": "LN001", "bucket_id": "B1" }
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("1", data["rows_affected"]);
        Assert.Equal("1", data["db_rows_affected"]);
        var count = Query<long>("SELECT COUNT(*) FROM loans WHERE loan_number = 'LN001'");
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // 2. UPDATE affects multiple rows
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Update_MultipleRows_ReturnsCorrectCount()
    {
        Execute("CREATE TABLE IF NOT EXISTS accounts (id TEXT, status TEXT)");
        Execute("INSERT INTO accounts VALUES ('A1', 'pending')");
        Execute("INSERT INTO accounts VALUES ('A2', 'pending')");
        Execute("INSERT INTO accounts VALUES ('A3', 'active')");

        var component = new DatabaseWriterComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "UPDATE accounts SET status = 'processed' WHERE status = 'pending'"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("2", data["rows_affected"]);
        Assert.Equal("2", data["db_rows_affected"]);
    }

    // -------------------------------------------------------------------------
    // 3. Scalar mode returns a single value
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ScalarMode_ReturnsScalarValue()
    {
        Execute("CREATE TABLE IF NOT EXISTS items (name TEXT)");
        Execute("INSERT INTO items VALUES ('alpha')");
        Execute("INSERT INTO items VALUES ('beta')");

        var component = new DatabaseWriterComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "SELECT COUNT(*) FROM items",
              "scalar": true,
              "output_key": "item_count"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("2", data["item_count"]);
        Assert.Equal("0", data["db_rows_affected"]);
    }

    // -------------------------------------------------------------------------
    // 4. Custom output_key writes result under the specified key
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CustomOutputKey_WritesUnderSpecifiedKey()
    {
        Execute("CREATE TABLE IF NOT EXISTS orders (order_id TEXT)");
        Execute("INSERT INTO orders VALUES ('O1')");

        var component = new DatabaseWriterComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "DELETE FROM orders WHERE order_id = 'O1'",
              "output_key": "deleted_count"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("1", data["deleted_count"]);
        Assert.DoesNotContain("rows_affected", data.Keys);
    }

    // -------------------------------------------------------------------------
    // 5. Placeholder resolution in connection_string and parameters
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PlaceholderResolution_InConnectionStringAndParameters()
    {
        Execute("CREATE TABLE IF NOT EXISTS records (ref_id TEXT, value TEXT)");

        var component = new DatabaseWriterComponent();
        var config = MakeConfig("""
            {
              "provider": "sqlite",
              "connection_string": "{{db_conn}}",
              "query": "INSERT INTO records (ref_id, value) VALUES (@ref_id, @value)",
              "parameters": { "ref_id": "{{ref_id}}", "value": "{{val}}" }
            }
            """);
        var data = new Dictionary<string, string>
        {
            ["db_conn"] = _connectionString,
            ["ref_id"] = "REF-42",
            ["val"] = "test-value"
        };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        var count = Query<long>("SELECT COUNT(*) FROM records WHERE ref_id = 'REF-42'");
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // 6. Missing provider → CONFIG_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MissingProvider_ReturnsConfigError()
    {
        var component = new DatabaseWriterComponent();
        var config = MakeConfig("""
            {
              "connection_string": "Data Source=:memory:",
              "query": "SELECT 1"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONFIG_ERROR", result.Error!.ErrorCode);
        Assert.Contains("provider", result.Error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // 7. Missing connection_string → CONFIG_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MissingConnectionString_ReturnsConfigError()
    {
        var component = new DatabaseWriterComponent();
        var config = MakeConfig("""
            {
              "provider": "sqlite",
              "query": "SELECT 1"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONFIG_ERROR", result.Error!.ErrorCode);
        Assert.Contains("connection_string", result.Error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // 8. Missing query → CONFIG_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MissingQuery_ReturnsConfigError()
    {
        var component = new DatabaseWriterComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONFIG_ERROR", result.Error!.ErrorCode);
        Assert.Contains("query", result.Error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // 9. Unknown provider → UNSUPPORTED_PROVIDER
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnknownProvider_ReturnsUnsupportedProvider()
    {
        var component = new DatabaseWriterComponent();
        var config = MakeConfig("""
            {
              "provider": "oracle",
              "connection_string": "Data Source=test",
              "query": "UPDATE foo SET x = 1"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("UNSUPPORTED_PROVIDER", result.Error!.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // 10. Bad connection string → CONNECTION_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BadConnectionString_ReturnsConnectionError()
    {
        var component = new DatabaseWriterComponent();
        var config = MakeConfig("""
            {
              "provider": "sqlite",
              "connection_string": "Data Source=/nonexistent_dir_hack13_writer/db.db;Mode=ReadOnly",
              "query": "SELECT 1"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONNECTION_ERROR", result.Error!.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // 11. SQL error → QUERY_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SqlError_ReturnsQueryError()
    {
        var component = new DatabaseWriterComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "UPDATE @@NONEXISTENT_TABLE## SET x = 1"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("QUERY_ERROR", result.Error!.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // 12. CancellationToken cancelled → rethrows
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancellationToken_Cancelled_Rethrows()
    {
        var component = new DatabaseWriterComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "SELECT 1"
            }
            """);
        var data = new Dictionary<string, string>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => component.ExecuteAsync(config, data, cts.Token));
    }

    // -------------------------------------------------------------------------
    // 13. OutputData contains only written keys (not pre-existing data dict keys)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OutputData_ContainsOnlyWrittenKeys()
    {
        Execute("CREATE TABLE IF NOT EXISTS tags (tag TEXT)");
        Execute("INSERT INTO tags VALUES ('x')");

        var component = new DatabaseWriterComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "DELETE FROM tags WHERE tag = 'x'"
            }
            """);
        var data = new Dictionary<string, string> { ["pre_existing"] = "value" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.DoesNotContain("pre_existing", result.OutputData.Keys);
        Assert.Contains("rows_affected", result.OutputData.Keys);
        Assert.Contains("db_rows_affected", result.OutputData.Keys);
    }
}
