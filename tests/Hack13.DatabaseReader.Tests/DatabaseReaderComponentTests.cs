using System.Text.Json;
using Hack13.DatabaseReader;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Models;
using Microsoft.Data.Sqlite;

namespace Hack13.DatabaseReader.Tests;

public class DatabaseReaderComponentTests : IDisposable
{
    private readonly string _dbName;
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAlive;

    public DatabaseReaderComponentTests()
    {
        _dbName = $"hack13_test_{Guid.NewGuid():N}";
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

    private static ComponentConfiguration MakeConfig(string json) => new()
    {
        ComponentType = "database_reader",
        ComponentVersion = "1.0",
        Config = JsonDocument.Parse(json).RootElement
    };

    // -------------------------------------------------------------------------
    // 1. Happy path: SQLite in-memory DB, insert row, read it back
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HappyPath_SqliteInMemory_ReadsFirstRow()
    {
        Execute("CREATE TABLE IF NOT EXISTS loans (loan_number TEXT, borrower TEXT, balance TEXT)");
        Execute("INSERT INTO loans VALUES ('LN001', 'Alice Smith', '150000.00')");

        var component = new DatabaseReaderComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "SELECT loan_number, borrower, balance FROM loans WHERE loan_number = @loan_number",
              "parameters": { "loan_number": "LN001" }
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("LN001", data["loan_number"]);
        Assert.Equal("Alice Smith", data["borrower"]);
        Assert.Equal("150000.00", data["balance"]);
        Assert.Equal("1", data["db_row_count"]);
    }

    // -------------------------------------------------------------------------
    // 2. require_row: true, no matching rows → NO_ROWS_RETURNED
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequireRow_True_NoRows_ReturnsFailure()
    {
        Execute("CREATE TABLE IF NOT EXISTS accounts (id TEXT)");

        var component = new DatabaseReaderComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "SELECT id FROM accounts WHERE id = 'nonexistent'",
              "require_row": true
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("NO_ROWS_RETURNED", result.Error!.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // 3. require_row: false, no rows → success with empty column output
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequireRow_False_NoRows_ReturnsSuccess()
    {
        Execute("CREATE TABLE IF NOT EXISTS items (name TEXT)");

        var component = new DatabaseReaderComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "SELECT name FROM items WHERE name = 'nonexistent'",
              "require_row": false
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("0", data["db_row_count"]);
        Assert.DoesNotContain("name", data.Keys);
    }

    // -------------------------------------------------------------------------
    // 4. Missing provider → CONFIG_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MissingProvider_ReturnsConfigError()
    {
        var component = new DatabaseReaderComponent();
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
    // 5. Missing connection_string → CONFIG_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MissingConnectionString_ReturnsConfigError()
    {
        var component = new DatabaseReaderComponent();
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
    // 6. Unknown provider → UNSUPPORTED_PROVIDER
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnknownProvider_ReturnsUnsupportedProvider()
    {
        var component = new DatabaseReaderComponent();
        var config = MakeConfig("""
            {
              "provider": "oracle",
              "connection_string": "Data Source=test",
              "query": "SELECT 1 FROM DUAL"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("UNSUPPORTED_PROVIDER", result.Error!.ErrorCode);
        Assert.Contains("oracle", result.Error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // 7. Bad connection string → CONNECTION_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BadConnectionString_ReturnsConnectionError()
    {
        var component = new DatabaseReaderComponent();
        var config = MakeConfig("""
            {
              "provider": "sqlite",
              "connection_string": "Data Source=/nonexistent_directory_hack13_test/database.db;Mode=ReadOnly",
              "query": "SELECT 1"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONNECTION_ERROR", result.Error!.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // 8. SQL syntax error → QUERY_ERROR
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SqlSyntaxError_ReturnsQueryError()
    {
        var component = new DatabaseReaderComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "SELECT * FROM @@INVALID_SYNTAX## WHERE !!!"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("QUERY_ERROR", result.Error!.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // 9. output_prefix applied to all column keys
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OutputPrefix_AppliedToAllColumnKeys()
    {
        Execute("CREATE TABLE IF NOT EXISTS customers (cust_id TEXT, cust_name TEXT)");
        Execute("INSERT INTO customers VALUES ('C001', 'Bob Jones')");

        var component = new DatabaseReaderComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "SELECT cust_id, cust_name FROM customers WHERE cust_id = 'C001'",
              "output_prefix": "db_"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("C001", data["db_cust_id"]);
        Assert.Equal("Bob Jones", data["db_cust_name"]);
        Assert.DoesNotContain("cust_id", data.Keys);
        Assert.DoesNotContain("cust_name", data.Keys);
    }

    // -------------------------------------------------------------------------
    // 10. db_row_count reflects total rows returned
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DbRowCount_ReflectsTotalRowCount()
    {
        Execute("CREATE TABLE IF NOT EXISTS orders (order_id TEXT, amount TEXT)");
        Execute("INSERT INTO orders VALUES ('O1', '100.00')");
        Execute("INSERT INTO orders VALUES ('O2', '200.00')");
        Execute("INSERT INTO orders VALUES ('O3', '300.00')");

        var component = new DatabaseReaderComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "SELECT order_id, amount FROM orders ORDER BY order_id"
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("3", data["db_row_count"]);
        // Column values are from the first row
        Assert.Equal("O1", data["order_id"]);
        Assert.Equal("100.00", data["amount"]);
    }

    // -------------------------------------------------------------------------
    // 11. CancellationToken cancellation is re-thrown
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancellationToken_Cancelled_Rethrows()
    {
        var component = new DatabaseReaderComponent();
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
    // 12. OutputData contains only written keys (not input data dictionary keys)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OutputData_ContainsOnlyWrittenKeys()
    {
        Execute("CREATE TABLE IF NOT EXISTS products (product_id TEXT, price TEXT)");
        Execute("INSERT INTO products VALUES ('P001', '49.99')");

        var component = new DatabaseReaderComponent();
        var config = MakeConfig($$"""
            {
              "provider": "sqlite",
              "connection_string": "{{_connectionString}}",
              "query": "SELECT product_id, price FROM products WHERE product_id = 'P001'"
            }
            """);
        var data = new Dictionary<string, string> { ["some_existing_key"] = "existing_value" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.DoesNotContain("some_existing_key", result.OutputData.Keys);
        Assert.Contains("product_id", result.OutputData.Keys);
        Assert.Contains("price", result.OutputData.Keys);
        Assert.Contains("db_row_count", result.OutputData.Keys);
    }
}
