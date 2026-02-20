using System.Diagnostics;
using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Interfaces;
using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;
using Hack13.Database.Common;

namespace Hack13.DatabaseReader;

public class DatabaseReaderComponent : IComponent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string ComponentType => "database_reader";

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
            var dbConfig = config.Config.Deserialize<DatabaseReaderConfig>(JsonOptions)
                ?? throw new InvalidOperationException("Database reader configuration is null.");

            if (string.IsNullOrWhiteSpace(dbConfig.Provider))
                return Failure("CONFIG_ERROR", "Required field 'provider' is missing.", null, sw);

            if (string.IsNullOrWhiteSpace(dbConfig.ConnectionString))
                return Failure("CONFIG_ERROR", "Required field 'connection_string' is missing.", null, sw);

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
                return Failure("UNSUPPORTED_PROVIDER", $"Unsupported database provider: '{dbConfig.Provider}'.", null, sw);
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
                    return Failure("CONNECTION_ERROR", $"Failed to open database connection: {ex.Message}", null, sw);
                }

                logs.Add(MakeLog(LogLevel.Debug, "Connection opened. Preparing query."));

                using var command = connection.CreateCommand();
                command.CommandText = resolvedQuery;
                command.CommandTimeout = dbConfig.CommandTimeoutSeconds;

                foreach (var (key, value) in resolvedParams)
                {
                    var param = command.CreateParameter();
                    param.ParameterName = key.StartsWith("@") ? key : $"@{key}";
                    param.Value = value;
                    command.Parameters.Add(param);
                }

                System.Data.Common.DbDataReader reader;
                try
                {
                    reader = await command.ExecuteReaderAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Failure("QUERY_ERROR", $"Query execution failed: {ex.Message}", null, sw);
                }

                await using (reader)
                {
                    var prefix = dbConfig.OutputPrefix ?? string.Empty;
                    var rowCount = 0;

                    if (dbConfig.MultiRow)
                    {
                        var rows = new List<Dictionary<string, string>>();
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var row = new Dictionary<string, string>();
                            for (var i = 0; i < reader.FieldCount; i++)
                            {
                                var columnName = reader.GetName(i);
                                var columnValue = reader.IsDBNull(i)
                                    ? string.Empty
                                    : reader.GetValue(i)?.ToString() ?? string.Empty;
                                row[$"{prefix}{columnName}"] = columnValue;
                            }
                            rows.Add(row);
                        }

                        rowCount = rows.Count;

                        if (rowCount == 0 && dbConfig.RequireRow)
                            return Failure("NO_ROWS_RETURNED", "Query returned no rows but require_row is true.", null, sw);

                        var rowsKey = dbConfig.RowsOutputKey;
                        var rowsJson = JsonSerializer.Serialize(rows);
                        outputData[rowsKey] = rowsJson;
                        dataDictionary[rowsKey] = rowsJson;

                        logs.Add(MakeLog(LogLevel.Info, $"Multi-row mode: serialized {rowCount} row(s) to '{rowsKey}'."));
                    }
                    else
                    {
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            rowCount++;

                            for (var i = 0; i < reader.FieldCount; i++)
                            {
                                var columnName = reader.GetName(i);
                                var columnValue = reader.IsDBNull(i)
                                    ? string.Empty
                                    : reader.GetValue(i)?.ToString() ?? string.Empty;
                                var outputKey = $"{prefix}{columnName}";

                                outputData[outputKey] = columnValue;
                                dataDictionary[outputKey] = columnValue;

                                logs.Add(MakeLog(LogLevel.Debug, $"Column '{columnName}' → '{outputKey}' = '{columnValue}'"));
                            }

                            while (await reader.ReadAsync(cancellationToken))
                                rowCount++;
                        }

                        if (rowCount == 0 && dbConfig.RequireRow)
                            return Failure("NO_ROWS_RETURNED", "Query returned no rows but require_row is true.", null, sw);
                    }

                    outputData["db_row_count"] = rowCount.ToString();
                    dataDictionary["db_row_count"] = rowCount.ToString();

                    logs.Add(MakeLog(LogLevel.Info, $"Query returned {rowCount} row(s)."));
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
            return Failure("CONFIG_ERROR", $"Invalid database reader configuration: {ex.Message}", null, sw);
        }
        catch (Exception ex)
        {
            return Failure("UNEXPECTED_ERROR", ex.Message, null, sw);
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

    private static ComponentResult Failure(string code, string message, string? stepDetail, Stopwatch sw) =>
        new()
        {
            Status = ComponentStatus.Failure,
            Error = new ComponentError { ErrorCode = code, ErrorMessage = message, StepDetail = stepDetail },
            DurationMs = sw.ElapsedMilliseconds
        };

    private static LogEntry MakeLog(LogLevel level, string message) =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            ComponentType = "database_reader",
            Level = level,
            Message = message
        };
}
