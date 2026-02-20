using System.Diagnostics;
using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Interfaces;
using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;
using Hack13.Database.Common;

namespace Hack13.DatabaseWriter;

public class DatabaseWriterComponent : IComponent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string ComponentType => "database_writer";

    public async Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var logs = new List<LogEntry>();
        var outputData = new Dictionary<string, string>();

        try
        {
            var dbConfig = config.Config.Deserialize<DatabaseWriterConfig>(JsonOptions)
                ?? throw new InvalidOperationException("Database writer configuration is null.");

            if (string.IsNullOrWhiteSpace(dbConfig.Provider))
                return Failure("CONFIG_ERROR", "Required field 'provider' is missing.", sw);

            if (string.IsNullOrWhiteSpace(dbConfig.ConnectionString))
                return Failure("CONFIG_ERROR", "Required field 'connection_string' is missing.", sw);

            if (string.IsNullOrWhiteSpace(dbConfig.Query))
                return Failure("CONFIG_ERROR", "Required field 'query' is missing.", sw);

            var resolvedConnectionString = PlaceholderResolver.Resolve(dbConfig.ConnectionString, dataDictionary);
            var resolvedQuery = PlaceholderResolver.Resolve(dbConfig.Query, dataDictionary);

            var resolvedParams = new Dictionary<string, string>();
            if (dbConfig.Parameters != null)
            {
                foreach (var (key, value) in dbConfig.Parameters)
                    resolvedParams[key] = PlaceholderResolver.Resolve(value, dataDictionary);
            }

            logs.Add(MakeLog(LogLevel.Info, $"Connecting to '{dbConfig.Provider}' database."));

            System.Data.Common.DbConnection connection;
            try
            {
                connection = DbConnectionFactory.Create(dbConfig.Provider, resolvedConnectionString);
            }
            catch (InvalidOperationException)
            {
                return Failure("UNSUPPORTED_PROVIDER", $"Unsupported database provider: '{dbConfig.Provider}'.", sw);
            }

            await using (connection)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await connection.OpenAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Failure("CONNECTION_ERROR", $"Failed to open database connection: {ex.Message}", sw);
                }

                logs.Add(MakeLog(LogLevel.Debug, "Connection opened. Preparing command."));

                using var command = connection.CreateCommand();
                command.CommandText = resolvedQuery;
                command.CommandTimeout = dbConfig.CommandTimeoutSeconds;

                foreach (var (key, value) in resolvedParams)
                {
                    var param = command.CreateParameter();
                    param.ParameterName = key.StartsWith('@') ? key : $"@{key}";
                    param.Value = value;
                    command.Parameters.Add(param);
                }

                try
                {
                    if (dbConfig.Scalar)
                    {
                        var scalarResult = await command.ExecuteScalarAsync(cancellationToken);
                        var scalarValue = scalarResult?.ToString() ?? string.Empty;

                        outputData[dbConfig.OutputKey] = scalarValue;
                        dataDictionary[dbConfig.OutputKey] = scalarValue;
                        outputData["db_rows_affected"] = "0";
                        dataDictionary["db_rows_affected"] = "0";

                        logs.Add(MakeLog(LogLevel.Info, $"Scalar result written to '{dbConfig.OutputKey}': '{scalarValue}'."));
                    }
                    else
                    {
                        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                        var rowsStr = rowsAffected.ToString();

                        outputData[dbConfig.OutputKey] = rowsStr;
                        dataDictionary[dbConfig.OutputKey] = rowsStr;
                        outputData["db_rows_affected"] = rowsStr;
                        dataDictionary["db_rows_affected"] = rowsStr;

                        logs.Add(MakeLog(LogLevel.Info, $"Non-query executed. Rows affected: {rowsAffected}. Result written to '{dbConfig.OutputKey}'."));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Failure("QUERY_ERROR", $"Query execution failed: {ex.Message}", sw);
                }
            }

            return Success(outputData, logs, sw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            return Failure("CONFIG_ERROR", $"Invalid database writer configuration: {ex.Message}", sw);
        }
        catch (Exception ex)
        {
            return Failure("UNEXPECTED_ERROR", ex.Message, sw);
        }
    }

    private static ComponentResult Success(Dictionary<string, string> outputData, List<LogEntry> logs, Stopwatch sw) =>
        new()
        {
            Status = ComponentStatus.Success,
            OutputData = outputData,
            LogEntries = logs,
            DurationMs = sw.ElapsedMilliseconds
        };

    private static ComponentResult Failure(string code, string message, Stopwatch sw) =>
        new()
        {
            Status = ComponentStatus.Failure,
            Error = new ComponentError { ErrorCode = code, ErrorMessage = message },
            DurationMs = sw.ElapsedMilliseconds
        };

    private static LogEntry MakeLog(LogLevel level, string message) =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            ComponentType = "database_writer",
            Level = level,
            Message = message
        };
}
