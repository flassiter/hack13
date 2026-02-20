using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Interfaces;
using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;

namespace Hack13.HttpClient;

public class HttpClientComponent : IComponent
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

    private static readonly string[] ValidMethods =
        ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public string ComponentType => "http_client";

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
            var httpConfig = config.Config.Deserialize<HttpClientConfig>(JsonOptions)
                ?? throw new InvalidOperationException("HTTP client configuration is null.");

            if (string.IsNullOrWhiteSpace(httpConfig.Url))
                return Failure("CONFIG_ERROR", "Required field 'url' is missing.", sw);

            var method = (httpConfig.Method ?? "GET").ToUpperInvariant();
            if (!ValidMethods.Contains(method))
                return Failure("CONFIG_ERROR", $"Invalid HTTP method: '{httpConfig.Method}'.", sw);

            var resolvedUrl = PlaceholderResolver.Resolve(httpConfig.Url, dataDictionary);
            var resolvedBody = httpConfig.Body != null
                ? PlaceholderResolver.Resolve(httpConfig.Body, dataDictionary)
                : null;

            var resolvedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (httpConfig.Headers != null)
            {
                foreach (var (key, value) in httpConfig.Headers)
                    resolvedHeaders[key] = PlaceholderResolver.Resolve(value, dataDictionary);
            }

            logs.Add(MakeLog(LogLevel.Info, $"Sending {method} request to '{resolvedUrl}'."));

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(httpConfig.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            using var request = new HttpRequestMessage(new HttpMethod(method), resolvedUrl);

            foreach (var (key, value) in resolvedHeaders)
            {
                if (!key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    request.Headers.TryAddWithoutValidation(key, value);
            }

            if (resolvedBody != null)
            {
                var contentType = resolvedHeaders.TryGetValue("Content-Type", out var ct)
                    ? ct
                    : "application/json";
                request.Content = new StringContent(resolvedBody, Encoding.UTF8, contentType);
            }

            HttpResponseMessage response;
            string responseBody;
            int statusCode;
            bool isSuccessStatusCode;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                response = await SharedClient.SendAsync(request, linkedCts.Token);
                using (response)
                {
                    statusCode = (int)response.StatusCode;
                    isSuccessStatusCode = response.IsSuccessStatusCode;
                    responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token);
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return Failure("REQUEST_FAILED", $"HTTP request timed out after {httpConfig.TimeoutSeconds} seconds.", sw);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Failure("REQUEST_FAILED", $"HTTP request failed: {ex.Message}", sw);
            }

            outputData["http_status_code"] = statusCode.ToString();
            dataDictionary["http_status_code"] = statusCode.ToString();

            var isSuccess = httpConfig.SuccessStatusCodes?.Count > 0
                ? httpConfig.SuccessStatusCodes.Contains(statusCode)
                : isSuccessStatusCode;

            if (!isSuccess)
                return FailureWithOutput("HTTP_ERROR", $"Unexpected HTTP status code: {statusCode}.", sw, outputData);

            if (!string.IsNullOrEmpty(httpConfig.ResponseBodyKey))
            {
                outputData[httpConfig.ResponseBodyKey] = responseBody;
                dataDictionary[httpConfig.ResponseBodyKey] = responseBody;
            }

            if (httpConfig.ResponseFieldMap?.Count > 0 && !string.IsNullOrWhiteSpace(responseBody))
            {
                foreach (var (outputKey, jsonPath) in httpConfig.ResponseFieldMap)
                {
                    try
                    {
                        var value = ExtractJsonValue(responseBody, jsonPath);
                        if (value != null)
                        {
                            outputData[outputKey] = value;
                            dataDictionary[outputKey] = value;
                            logs.Add(MakeLog(LogLevel.Debug, $"Mapped '{jsonPath}' → '{outputKey}' = '{value}'."));
                        }
                        else
                        {
                            logs.Add(MakeLog(LogLevel.Warn, $"JSON path '{jsonPath}' not found in response."));
                        }
                    }
                    catch (JsonException ex)
                    {
                        return FailureWithOutput("RESPONSE_PARSE_ERROR",
                            $"Failed to parse response for path '{jsonPath}': {ex.Message}", sw, outputData);
                    }
                }
            }

            logs.Add(MakeLog(LogLevel.Info, $"Request completed with status {statusCode}."));

            return Success(outputData, logs, sw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            return Failure("CONFIG_ERROR", $"Invalid HTTP client configuration: {ex.Message}", sw);
        }
        catch (Exception ex)
        {
            return Failure("UNEXPECTED_ERROR", ex.Message, sw);
        }
    }

    internal static string? ExtractJsonValue(string json, string path)
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
                if (endBracket < 0)
                    return null;
                if (!int.TryParse(segment[(bracketPos + 1)..endBracket], out var idx))
                    return null;

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
            ComponentType = "http_client",
            Level = level,
            Message = message
        };
}
