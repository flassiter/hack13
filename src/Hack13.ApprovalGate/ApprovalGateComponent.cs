using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Interfaces;
using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;

namespace Hack13.ApprovalGate;

public class ApprovalGateComponent : IComponent
{
    private static readonly System.Net.Http.HttpClient SharedClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string ComponentType => "approval_gate";

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
            var agConfig = config.Config.Deserialize<ApprovalGateConfig>(JsonOptions)
                ?? throw new InvalidOperationException("Approval gate configuration is null.");

            if (string.IsNullOrWhiteSpace(agConfig.PollUrl))
                return Failure("CONFIG_ERROR", "Required field 'poll_url' is missing.", sw);

            if (string.IsNullOrWhiteSpace(agConfig.ApprovedPath))
                return Failure("CONFIG_ERROR", "Required field 'approved_path' is missing.", sw);

            if (string.IsNullOrWhiteSpace(agConfig.ApprovedValue))
                return Failure("CONFIG_ERROR", "Required field 'approved_value' is missing.", sw);

            var resolvedUrl = PlaceholderResolver.Resolve(agConfig.PollUrl, dataDictionary);

            var resolvedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (agConfig.PollHeaders != null)
            {
                foreach (var (key, value) in agConfig.PollHeaders)
                    resolvedHeaders[key] = PlaceholderResolver.Resolve(value, dataDictionary);
            }

            var pollInterval = TimeSpan.FromSeconds(agConfig.PollIntervalSeconds);
            var deadline = DateTime.UtcNow.AddSeconds(agConfig.TimeoutSeconds);
            var pollCount = 0;

            logs.Add(MakeLog(LogLevel.Info,
                $"Starting approval polling: {agConfig.PollMethod} {resolvedUrl} " +
                $"(interval={agConfig.PollIntervalSeconds}s, timeout={agConfig.TimeoutSeconds}s)"));

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (DateTime.UtcNow >= deadline)
                {
                    WriteStatus(outputData, dataDictionary, "timeout", pollCount);
                    return FailureWithOutput("TIMEOUT",
                        $"Approval timed out after {agConfig.TimeoutSeconds} seconds ({pollCount} polls).",
                        sw, outputData);
                }

                pollCount++;
                string? responseBody = null;

                try
                {
                    using var request = new HttpRequestMessage(
                        new HttpMethod(agConfig.PollMethod), resolvedUrl);

                    foreach (var (key, value) in resolvedHeaders)
                    {
                        if (!key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            request.Headers.TryAddWithoutValidation(key, value);
                    }

                    using var pollCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, pollCts.Token);

                    using var response = await SharedClient.SendAsync(request, linked.Token);

                    if (response.IsSuccessStatusCode)
                        responseBody = await response.Content.ReadAsStringAsync(linked.Token);
                    else
                        logs.Add(MakeLog(LogLevel.Warn,
                            $"Poll {pollCount}: HTTP {(int)response.StatusCode} — transient error, retrying."));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logs.Add(MakeLog(LogLevel.Warn,
                        $"Poll {pollCount}: Request failed — {ex.Message}. Retrying."));
                }

                if (responseBody != null)
                {
                    var approvedVal = ExtractJsonValue(responseBody, agConfig.ApprovedPath);
                    if (string.Equals(approvedVal, agConfig.ApprovedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteStatus(outputData, dataDictionary, "approved", pollCount);
                        logs.Add(MakeLog(LogLevel.Info, $"Approval received after {pollCount} poll(s)."));
                        return Success(outputData, logs, sw);
                    }

                    if (!string.IsNullOrWhiteSpace(agConfig.RejectedPath) &&
                        !string.IsNullOrWhiteSpace(agConfig.RejectedValue))
                    {
                        var rejectedVal = ExtractJsonValue(responseBody, agConfig.RejectedPath);
                        if (string.Equals(rejectedVal, agConfig.RejectedValue, StringComparison.OrdinalIgnoreCase))
                        {
                            WriteStatus(outputData, dataDictionary, "rejected", pollCount);
                            logs.Add(MakeLog(LogLevel.Info, $"Rejection received after {pollCount} poll(s)."));
                            return FailureWithOutput("REJECTED", "Approval was rejected.", sw, outputData);
                        }
                    }

                    logs.Add(MakeLog(LogLevel.Debug,
                        $"Poll {pollCount}: Status not yet determined. Waiting {agConfig.PollIntervalSeconds}s."));
                }

                var remaining = deadline - DateTime.UtcNow;
                var delay = remaining < pollInterval ? remaining : pollInterval;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            return Failure("CONFIG_ERROR", $"Invalid approval gate configuration: {ex.Message}", sw);
        }
        catch (Exception ex)
        {
            return Failure("UNEXPECTED_ERROR", ex.Message, sw);
        }
    }

    private static void WriteStatus(
        Dictionary<string, string> outputData,
        Dictionary<string, string> dataDictionary,
        string status,
        int pollCount)
    {
        outputData["approval_status"] = status;
        dataDictionary["approval_status"] = status;
        outputData["approval_poll_count"] = pollCount.ToString();
        dataDictionary["approval_poll_count"] = pollCount.ToString();
    }

    private static string? ExtractJsonValue(string json, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var normalizedPath = path.StartsWith("$.") ? path[2..]
                : path.TrimStart('$').TrimStart('.');

            if (string.IsNullOrEmpty(normalizedPath))
                return doc.RootElement.GetRawText();

            var current = doc.RootElement;

            foreach (var segment in normalizedPath.Split('.'))
            {
                var bracketPos = segment.IndexOf('[');
                if (bracketPos >= 0)
                {
                    var propName = segment[..bracketPos];
                    var endBracket = segment.IndexOf(']', bracketPos);
                    var idx = int.Parse(segment[(bracketPos + 1)..endBracket]);

                    if (!string.IsNullOrEmpty(propName))
                    {
                        if (!current.TryGetProperty(propName, out current))
                            return null;
                    }

                    if (current.ValueKind != JsonValueKind.Array || idx >= current.GetArrayLength())
                        return null;

                    current = current[idx];
                }
                else
                {
                    if (!current.TryGetProperty(segment, out current))
                        return null;
                }
            }

            return current.ValueKind == JsonValueKind.String
                ? current.GetString()
                : current.GetRawText();
        }
        catch
        {
            return null;
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

    private static ComponentResult FailureWithOutput(string code, string message, Stopwatch sw, Dictionary<string, string> outputData) =>
        new()
        {
            Status = ComponentStatus.Failure,
            Error = new ComponentError { ErrorCode = code, ErrorMessage = message },
            OutputData = outputData,
            DurationMs = sw.ElapsedMilliseconds
        };

    private static LogEntry MakeLog(LogLevel level, string message) =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            ComponentType = "approval_gate",
            Level = level,
            Message = message
        };
}
