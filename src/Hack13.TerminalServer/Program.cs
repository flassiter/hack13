using Microsoft.Extensions.Logging;
using System.Net;
using Hack13.TerminalServer.Engine;
using Hack13.TerminalServer.Navigation;
using Hack13.TerminalServer.Server;

// Resolve config paths relative to the executable or from command-line argument
var configBase = args.Length > 0 ? args[0] : FindConfigBase();

var screenCatalogDir = Path.Combine(configBase, "configs", "screen-catalog");
var navigationPath = Path.Combine(configBase, "configs", "navigation.json");
var testDataPath = Path.Combine(configBase, "test-data", "loans.json");

int port = 5250;
if (args.Length > 1 && int.TryParse(args[1], out var customPort))
    port = customPort;
var bindAddress = IPAddress.Loopback;
if (args.Length > 2 && IPAddress.TryParse(args[2], out var customBindAddress))
    bindAddress = customBindAddress;

// Set up logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddConsole()
        .SetMinimumLevel(LogLevel.Information);
});
var logger = loggerFactory.CreateLogger("MockServer");
logger.LogInformation("Starting mock server on {Address}:{Port}", bindAddress, port);

// Load configuration
logger.LogInformation("Loading screen catalog from: {Path}", screenCatalogDir);
var screenLoader = new ScreenLoader();
screenLoader.LoadFromDirectory(screenCatalogDir);
logger.LogInformation("Loaded {Count} screens: {Screens}",
    screenLoader.AllScreens.Count,
    string.Join(", ", screenLoader.AllScreens.Keys));

logger.LogInformation("Loading navigation config from: {Path}", navigationPath);
var navConfig = NavigationConfig.LoadFromFile(navigationPath);
logger.LogInformation("Loaded {Count} transitions, {CredCount} credentials",
    navConfig.Transitions.Count, navConfig.Credentials.Count);

logger.LogInformation("Loading test data from: {Path}", testDataPath);
var testData = new TestDataStore();
testData.LoadFromFile(testDataPath);

// Start server
var server = new Tn5250Server(port, screenLoader, navConfig, testData, loggerFactory, bindAddress);
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    logger.LogInformation("Shutdown requested...");
    cts.Cancel();
};

await server.RunAsync(cts.Token);

static string FindConfigBase()
{
    // Walk up from executable directory looking for configs/
    var dir = AppDomain.CurrentDomain.BaseDirectory;
    for (int i = 0; i < 8; i++)
    {
        if (Directory.Exists(Path.Combine(dir, "configs")))
            return dir;
        var parent = Directory.GetParent(dir);
        if (parent == null) break;
        dir = parent.FullName;
    }

    // Fallback: try current working directory
    if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "configs")))
        return Directory.GetCurrentDirectory();

    throw new FileNotFoundException(
        "Cannot find configs/ directory. Pass the project root as the first argument.");
}
