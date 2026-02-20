using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Protocol;
using Hack13.Contracts.Models;
using Hack13.Contracts.ScreenCatalog;
using Hack13.Contracts.Utilities;
using Hack13.TerminalClient.Protocol;
using ScreenIdent = Hack13.TerminalClient.Screen.ScreenIdentifier;

namespace Hack13.TerminalClient.Workflow;

/// <summary>
/// Executes a sequence of navigate/scrape/assert steps against a TN5250 connection.
/// </summary>
public class WorkflowEngine
{
    private readonly ConnectorConfig _config;
    private readonly Dictionary<string, string> _parameters;
    private readonly List<ScreenDefinition> _screenDefinitions;
    private readonly List<LogEntry> _logEntries = new();
    private readonly Dictionary<string, string> _scrapedData = new();

    private ScreenBuffer _screenBuffer = new();
    private ScreenIdent _screenIdentifier = null!;
    private DataStreamParser _parser = null!;
    private InputEncoder _encoder = new();

    public IReadOnlyList<LogEntry> LogEntries => _logEntries;
    public IReadOnlyDictionary<string, string> ScrapedData => _scrapedData;

    public WorkflowEngine(ConnectorConfig config, Dictionary<string, string> parameters,
        List<ScreenDefinition> screenDefinitions)
    {
        _config = config;
        _parameters = parameters;
        _screenDefinitions = screenDefinitions;
    }

    public async Task<ComponentResult> ExecuteAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _screenIdentifier = new ScreenIdent(_screenDefinitions);
        _parser = new DataStreamParser(msg => Log(LogLevel.Debug, null, msg));

        var host = PlaceholderResolver.Resolve(_config.Connection.Host, _parameters);
        var terminalType = PlaceholderResolver.Resolve(_config.Connection.TerminalType, _parameters);
        var deviceName = string.IsNullOrWhiteSpace(_config.Connection.DeviceName)
            ? null
            : PlaceholderResolver.Resolve(_config.Connection.DeviceName, _parameters);
        var port = _config.Connection.Port;
        if (_parameters.TryGetValue("port", out var portStr) && int.TryParse(portStr, out var overridePort))
            port = overridePort;

        Log(LogLevel.Info, null, $"Connecting to {host}:{port}");

        try
        {
            using var client = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(_config.Connection.ConnectTimeoutSeconds));

            await client.ConnectAsync(host, port, connectCts.Token);
            Log(LogLevel.Info, null, "Connected, starting telnet negotiation");

            // Resolve the data stream: plain TCP or TLS
            Stream stream = client.GetStream();
            SslStream? sslStream = null;

            if (_config.Connection.UseTls)
            {
                Log(LogLevel.Info, null, "Starting TLS handshake");
                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                };

                if (_config.Connection.InsecureSkipVerify)
                {
                    sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                }
                else if (!string.IsNullOrWhiteSpace(_config.Connection.CaCertificatePath))
                {
                    var caPath = PlaceholderResolver.Resolve(_config.Connection.CaCertificatePath, _parameters);
                    var caCert = new X509Certificate2(caPath);
                    var caCollection = new X509Certificate2Collection { caCert };
                    sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
                    {
                        if (errors == SslPolicyErrors.None) return true;
                        if (cert == null || chain == null) return false;
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.AddRange(caCollection);
                        return chain.Build(new X509Certificate2(cert));
                    };
                }

                sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsClientAsync(sslOptions, ct);
                stream = sslStream;
                Log(LogLevel.Info, null, $"TLS established: {sslStream.SslProtocol}, {sslStream.CipherAlgorithm}");
            }

            try
            {

            // Telnet negotiation
            var negotiator = new ClientTelnetNegotiator(stream,
                msg => Log(LogLevel.Debug, null, msg),
                terminalType,
                deviceName);
            await negotiator.NegotiateAsync(ct);
            Log(LogLevel.Info, null, "Telnet negotiation complete");

            // Receive initial screen
            using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            responseCts.CancelAfter(TimeSpan.FromSeconds(_config.Connection.ResponseTimeoutSeconds));
            await _parser.ReadAndParseScreenAsync(stream, _screenBuffer, negotiator.ConsumePendingData(), responseCts.Token);

            var initialScreen = _screenIdentifier.Identify(_screenBuffer);
            Log(LogLevel.Info, null, $"Initial screen: {initialScreen?.ScreenId ?? "unknown"}");

            // Execute workflow steps
            foreach (var step in _config.Steps)
            {
                var stepResult = await ExecuteStepAsync(stream, step, ct);
                if (!stepResult)
                {
                    sw.Stop();
                    return new ComponentResult
                    {
                        Status = ComponentStatus.Failure,
                        OutputData = new Dictionary<string, string>(_scrapedData),
                        Error = new ComponentError
                        {
                            ErrorCode = "STEP_FAILED",
                            ErrorMessage = $"Step '{step.StepName}' failed",
                            StepDetail = step.StepName
                        },
                        LogEntries = _logEntries,
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }
            }

            sw.Stop();
            Log(LogLevel.Info, null, $"Workflow complete, scraped {_scrapedData.Count} fields in {sw.ElapsedMilliseconds}ms");

            return new ComponentResult
            {
                Status = ComponentStatus.Success,
                OutputData = new Dictionary<string, string>(_scrapedData),
                LogEntries = _logEntries,
                DurationMs = sw.ElapsedMilliseconds
            };
            }
            finally
            {
                if (sslStream != null) await sslStream.DisposeAsync();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            return new ComponentResult
            {
                Status = ComponentStatus.Failure,
                OutputData = new Dictionary<string, string>(_scrapedData),
                Error = new ComponentError
                {
                    ErrorCode = "CANCELLED",
                    ErrorMessage = "Workflow was cancelled"
                },
                LogEntries = _logEntries,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log(LogLevel.Error, null, $"Connection error: {ex.Message}");
            return new ComponentResult
            {
                Status = ComponentStatus.Failure,
                OutputData = new Dictionary<string, string>(_scrapedData),
                Error = new ComponentError
                {
                    ErrorCode = "CONNECTION_ERROR",
                    ErrorMessage = ex.Message
                },
                LogEntries = _logEntries,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private async Task<bool> ExecuteStepAsync(Stream stream, WorkflowStep step, CancellationToken ct)
    {
        Log(LogLevel.Info, step.StepName, $"Executing {step.Type} step: {step.StepName}");

        for (int attempt = 1; attempt <= step.MaxRetries; attempt++)
        {
            if (attempt > 1)
                Log(LogLevel.Info, step.StepName, $"Retry attempt {attempt}/{step.MaxRetries}");

            bool success = step.Type switch
            {
                StepType.Navigate => await ExecuteNavigateAsync(stream, step, ct),
                StepType.Scrape => ExecuteScrape(step),
                StepType.Assert => ExecuteAssert(step),
                _ => throw new InvalidOperationException($"Unknown step type: {step.Type}")
            };

            if (success) return true;

            if (attempt < step.MaxRetries)
                await Task.Delay(500, ct);
        }

        return false;
    }

    private async Task<bool> ExecuteNavigateAsync(Stream stream, WorkflowStep step, CancellationToken ct)
    {
        // Identify current screen to find field positions
        var currentScreen = _screenIdentifier.Identify(_screenBuffer);
        if (currentScreen == null)
        {
            Log(LogLevel.Error, step.StepName, "Cannot identify current screen");
            return false;
        }

        Log(LogLevel.Debug, step.StepName, $"Current screen: {currentScreen.ScreenId}");

        // Build input fields from step config
        var inputFields = new List<InputField>();

        if (step.Fields != null)
        {
            foreach (var (fieldName, rawValue) in step.Fields)
            {
                var value = PlaceholderResolver.Resolve(rawValue, _parameters);

                // Find the field position from the screen definition
                var fieldDef = currentScreen.Fields.FirstOrDefault(f =>
                    f.Name == fieldName && f.Type == "input");

                if (fieldDef == null)
                {
                    Log(LogLevel.Error, step.StepName, $"Input field '{fieldName}' not found on screen '{currentScreen.ScreenId}'");
                    return false;
                }

                // Data starts at fieldDef.Col + 1 (after SF attribute byte)
                var padded = value.PadRight(fieldDef.Length);
                if (padded.Length > fieldDef.Length)
                    padded = padded[..fieldDef.Length];

                inputFields.Add(new InputField
                {
                    Row = fieldDef.Row,
                    Col = fieldDef.Col + 1,
                    Value = padded
                });

                var isSensitive = fieldDef.Attributes?.Contains("hidden", StringComparison.OrdinalIgnoreCase) == true ||
                                  fieldName.Contains("password", StringComparison.OrdinalIgnoreCase);
                Log(LogLevel.Debug, step.StepName,
                    $"Set field '{fieldName}' = '{(isSensitive ? "***" : value)}'");
            }
        }

        // Resolve AID key
        if (string.IsNullOrEmpty(step.AidKey))
        {
            Log(LogLevel.Error, step.StepName, "Navigate step requires aid_key");
            return false;
        }
        var aidByte = Tn5250Constants.AidKeyFromName(step.AidKey);

        // Send input
        await _encoder.SendInputAsync(stream, aidByte, _screenBuffer.CursorRow, _screenBuffer.CursorCol,
            inputFields, ct);
        Log(LogLevel.Debug, step.StepName, $"Sent {step.AidKey} with {inputFields.Count} field(s)");

        // Read response with timeout
        using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        responseCts.CancelAfter(TimeSpan.FromSeconds(_config.Connection.ResponseTimeoutSeconds));

        _screenBuffer = new ScreenBuffer();
        await _parser.ReadAndParseScreenAsync(stream, _screenBuffer, responseCts.Token);

        // Verify expected screen
        if (!string.IsNullOrEmpty(step.ExpectScreen))
        {
            var newScreen = _screenIdentifier.Identify(_screenBuffer);
            if (newScreen == null || newScreen.ScreenId != step.ExpectScreen)
            {
                var actualId = newScreen?.ScreenId ?? "unknown";
                Log(LogLevel.Error, step.StepName,
                    $"Expected screen '{step.ExpectScreen}' but got '{actualId}'");

                // Check for error message on line 24
                var line24 = _screenBuffer.ReadRow(24).Trim();
                if (!string.IsNullOrEmpty(line24))
                    Log(LogLevel.Error, step.StepName, $"Screen error message: {line24}");

                return false;
            }

            Log(LogLevel.Info, step.StepName, $"Navigated to screen '{step.ExpectScreen}'");
        }

        return true;
    }

    private bool ExecuteScrape(WorkflowStep step)
    {
        if (step.ScrapeFields == null || step.ScrapeFields.Count == 0)
        {
            Log(LogLevel.Warn, step.StepName, "Scrape step has no fields defined");
            return true;
        }

        // Find the screen definition for field positions
        ScreenDefinition? screenDef = null;
        if (!string.IsNullOrEmpty(step.Screen))
        {
            screenDef = _screenDefinitions.FirstOrDefault(s => s.ScreenId == step.Screen);
        }
        else
        {
            screenDef = _screenIdentifier.Identify(_screenBuffer);
        }

        if (screenDef == null)
        {
            Log(LogLevel.Error, step.StepName, "Cannot determine screen for scraping");
            return false;
        }

        var missingFields = new List<string>();
        foreach (var fieldName in step.ScrapeFields)
        {
            var fieldDef = screenDef.Fields.FirstOrDefault(f => f.Name == fieldName);
            if (fieldDef == null)
            {
                missingFields.Add(fieldName);
                continue;
            }

            // Read from screen buffer: data is at (row, col+1) for field.Length chars
            // col+1 because col is where the SF attribute byte sits
            var value = _screenBuffer.ReadText(fieldDef.Row, fieldDef.Col + 1, fieldDef.Length).TrimEnd();
            _scrapedData[fieldName] = value;

            Log(LogLevel.Debug, step.StepName, $"Scraped '{fieldName}' = '{value}'");
        }

        if (missingFields.Count > 0)
        {
            Log(LogLevel.Error, step.StepName,
                $"Scrape failed: undefined field(s) on '{screenDef.ScreenId}': {string.Join(", ", missingFields)}");
            return false;
        }

        Log(LogLevel.Info, step.StepName, $"Scraped {step.ScrapeFields.Count} field(s) from '{screenDef.ScreenId}'");
        return true;
    }

    private bool ExecuteAssert(WorkflowStep step)
    {
        // Assert expected screen
        if (!string.IsNullOrEmpty(step.ExpectScreen))
        {
            if (!_screenIdentifier.IsScreen(_screenBuffer, step.ExpectScreen))
            {
                var actual = _screenIdentifier.Identify(_screenBuffer);
                Log(LogLevel.Error, step.StepName,
                    $"Assert failed: expected screen '{step.ExpectScreen}', got '{actual?.ScreenId ?? "unknown"}'");
                return false;
            }
            Log(LogLevel.Debug, step.StepName, $"Assert passed: screen is '{step.ExpectScreen}'");
        }

        // Assert no error text on screen
        if (!string.IsNullOrEmpty(step.ErrorText))
        {
            int row = step.ErrorRow ?? 24;
            var rowText = _screenBuffer.ReadRow(row);
            if (rowText.Contains(step.ErrorText, StringComparison.OrdinalIgnoreCase))
            {
                Log(LogLevel.Error, step.StepName,
                    $"Assert failed: error text '{step.ErrorText}' found on row {row}: {rowText.Trim()}");
                return false;
            }
            Log(LogLevel.Debug, step.StepName, $"Assert passed: error text '{step.ErrorText}' not found");
        }

        if (step.AssertFields is { Count: > 0 })
        {
            var screenDef = !string.IsNullOrWhiteSpace(step.Screen)
                ? _screenDefinitions.FirstOrDefault(s => s.ScreenId == step.Screen)
                : _screenIdentifier.Identify(_screenBuffer);

            if (screenDef == null)
            {
                Log(LogLevel.Error, step.StepName, "Assert failed: could not resolve screen for assert_fields.");
                return false;
            }

            var op = (step.AssertOperator ?? "equals").Trim().ToLowerInvariant();
            foreach (var (fieldName, expectedRaw) in step.AssertFields)
            {
                var fieldDef = screenDef.Fields.FirstOrDefault(f => f.Name == fieldName);
                if (fieldDef == null)
                {
                    Log(LogLevel.Error, step.StepName,
                        $"Assert failed: field '{fieldName}' is not defined on '{screenDef.ScreenId}'.");
                    return false;
                }

                var actual = _screenBuffer.ReadText(fieldDef.Row, fieldDef.Col + 1, fieldDef.Length).TrimEnd();
                var expected = PlaceholderResolver.Resolve(expectedRaw, _parameters);
                if (!EvaluateAssertion(op, actual, expected))
                {
                    Log(LogLevel.Error, step.StepName,
                        $"Assert failed for field '{fieldName}': actual='{actual}', expected='{expected}', op='{op}'.");
                    return false;
                }

                Log(LogLevel.Debug, step.StepName,
                    $"Assert passed for field '{fieldName}' with op '{op}'.");
            }
        }

        return true;
    }

    private static bool EvaluateAssertion(string op, string actual, string expected)
    {
        return op switch
        {
            "equals" => string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase),
            "not_equals" => !string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase),
            "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "starts_with" => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            "ends_with" => actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase)
        };
    }

    private void Log(LogLevel level, string? stepName, string message)
    {
        _logEntries.Add(new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            StepName = stepName,
            ComponentType = "green_screen_connector",
            Level = level,
            Message = message
        });
    }
}
