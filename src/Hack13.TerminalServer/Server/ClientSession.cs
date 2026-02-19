using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Hack13.Contracts.Protocol;
using Hack13.TerminalServer.Engine;
using Hack13.TerminalServer.Navigation;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Server;

/// <summary>
/// Handles a single TN5250 client connection.
/// Manages the telnet negotiation, screen display loop, and navigation.
/// </summary>
public class ClientSession
{
    private readonly TcpClient _client;
    private readonly ScreenLoader _screenLoader;
    private readonly ScreenRenderer _renderer;
    private readonly FieldExtractor _fieldExtractor;
    private readonly TransitionEvaluator _transitionEvaluator;
    private readonly TestDataStore _testData;
    private readonly NavigationConfig _navConfig;
    private readonly ILogger _logger;
    private readonly string _sessionId;

    public ClientSession(
        TcpClient client,
        ScreenLoader screenLoader,
        ScreenRenderer renderer,
        FieldExtractor fieldExtractor,
        TransitionEvaluator transitionEvaluator,
        TestDataStore testData,
        NavigationConfig navConfig,
        ILogger logger)
    {
        _client = client;
        _screenLoader = screenLoader;
        _renderer = renderer;
        _fieldExtractor = fieldExtractor;
        _transitionEvaluator = transitionEvaluator;
        _testData = testData;
        _navConfig = navConfig;
        _logger = logger;
        _sessionId = Guid.NewGuid().ToString("N")[..8];
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var endpoint = _client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogInformation("[{Session}] Client connected from {Endpoint}", _sessionId, endpoint);

        try
        {
            using var stream = _client.GetStream();

            // Phase 1: Telnet negotiation
            var negotiator = new TelnetNegotiator(stream, _logger);
            await negotiator.NegotiateAsync(ct);
            _logger.LogInformation("[{Session}] Negotiation complete, terminal: {Type}",
                _sessionId, negotiator.TerminalType);

            // Phase 2: Screen interaction loop
            var reader = new DataStreamReader(stream, _logger);
            var session = new SessionState { CurrentScreen = _navConfig.InitialScreen };

            // Display initial screen
            await SendScreenAsync(stream, session, null, ct);

            while (!ct.IsCancellationRequested)
            {
                // Read client input
                var input = await reader.ReadInputAsync(ct);
                _logger.LogInformation("[{Session}] Input: {Aid} on screen {Screen}",
                    _sessionId, input.AidKeyName, session.CurrentScreen);

                // Extract field values
                var currentScreen = _screenLoader.GetScreen(session.CurrentScreen);
                var fieldValues = _fieldExtractor.Extract(input, currentScreen);

                // Evaluate transition
                var result = _transitionEvaluator.Evaluate(session, input.AidKey, fieldValues);

                if (result.Success && result.TargetScreen != null)
                {
                    // Apply data updates to session
                    foreach (var kv in result.DataUpdates)
                    {
                        session.Data[kv.Key] = kv.Value;
                    }

                    // Load loan data into session if transitioning to a loan screen
                    if (session.Data.TryGetValue("loan_number", out var loanNum))
                    {
                        var loanData = _testData.GetLoanData(loanNum);
                        foreach (var kv in loanData)
                        {
                            session.Data[kv.Key] = kv.Value;
                        }
                    }

                    // Track authentication
                    if (session.CurrentScreen == "sign_on" && result.TargetScreen != "sign_on")
                    {
                        session.IsAuthenticated = true;
                        session.UserId = fieldValues.GetValueOrDefault("user_id");
                    }
                    else if (result.TargetScreen == "sign_on")
                    {
                        // Reset identity and session data on sign-out/navigation reset.
                        session.IsAuthenticated = false;
                        session.UserId = null;
                        session.Data.Clear();
                    }

                    session.CurrentScreen = result.TargetScreen;
                    await SendScreenAsync(stream, session, null, ct);
                }
                else
                {
                    // Re-display current screen with error message
                    await SendScreenAsync(stream, session, result.ErrorMessage, ct);
                }
            }
        }
        catch (IOException)
        {
            _logger.LogInformation("[{Session}] Client disconnected", _sessionId);
        }
        catch (TimeoutException)
        {
            _logger.LogInformation("[{Session}] Session idle timeout reached", _sessionId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[{Session}] Session cancelled", _sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Session}] Session error", _sessionId);
        }
        finally
        {
            _client.Close();
            _logger.LogInformation("[{Session}] Session ended", _sessionId);
        }
    }

    private async Task SendScreenAsync(NetworkStream stream, SessionState session, string? error, CancellationToken ct)
    {
        var screen = _screenLoader.GetScreen(session.CurrentScreen);
        var data = session.GetScreenData();
        var record = _renderer.RenderScreen(screen, data, error);

        _logger.LogDebug("[{Session}] Sending screen {ScreenId} ({Bytes} bytes)",
            _sessionId, session.CurrentScreen, record.Length);

        await stream.WriteAsync(record, ct);
        await stream.FlushAsync(ct);
    }
}
