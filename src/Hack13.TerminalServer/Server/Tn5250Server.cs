using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Hack13.TerminalServer.Engine;
using Hack13.TerminalServer.Navigation;

namespace Hack13.TerminalServer.Server;

/// <summary>
/// TCP listener that accepts TN5250 connections and spawns a ClientSession per connection.
/// </summary>
public class Tn5250Server
{
    private readonly int _port;
    private readonly IPAddress _bindAddress;
    private readonly ScreenLoader _screenLoader;
    private readonly ScreenRenderer _renderer;
    private readonly FieldExtractor _fieldExtractor;
    private readonly NavigationConfig _navConfig;
    private readonly TestDataStore _testData;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Task> _sessionTasks = new();

    public Tn5250Server(
        int port,
        ScreenLoader screenLoader,
        NavigationConfig navConfig,
        TestDataStore testData,
        ILoggerFactory loggerFactory,
        IPAddress? bindAddress = null)
    {
        _port = port;
        _bindAddress = bindAddress ?? IPAddress.Loopback;
        _screenLoader = screenLoader;
        _renderer = new ScreenRenderer();
        _fieldExtractor = new FieldExtractor();
        _navConfig = navConfig;
        _testData = testData;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<Tn5250Server>();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(_bindAddress, _port);
        listener.Start();
        _logger.LogInformation("TN5250 Mock Server listening on {Address}:{Port}", _bindAddress, _port);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);

                // Each connection runs independently and is tracked for clean shutdown.
                var sessionId = Guid.NewGuid().ToString("N")[..8];
                var sessionTask = HandleClientAsync(client, ct);
                _sessionTasks[sessionId] = sessionTask;
                _ = sessionTask.ContinueWith(t =>
                {
                    _sessionTasks.TryRemove(sessionId, out _);
                    if (t.IsFaulted)
                        _logger.LogError(t.Exception, "Session task {SessionTaskId} failed", sessionId);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Stop();
            await AwaitOutstandingSessionsAsync();
            _logger.LogInformation("Server stopped");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var transitionEvaluator = new TransitionEvaluator(
            _navConfig,
            _testData,
            _loggerFactory.CreateLogger<TransitionEvaluator>());

        var session = new ClientSession(
            client,
            _screenLoader,
            _renderer,
            _fieldExtractor,
            transitionEvaluator,
            _testData,
            _navConfig,
            _loggerFactory.CreateLogger<ClientSession>());

        await session.RunAsync(ct);
    }

    private async Task AwaitOutstandingSessionsAsync()
    {
        var tasks = _sessionTasks.Values.ToArray();
        if (tasks.Length == 0) return;

        _logger.LogInformation("Waiting for {Count} active session(s) to finish", tasks.Length);
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "One or more client sessions ended with errors during shutdown");
        }
    }
}
