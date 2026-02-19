using System.Text;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Hack13.PdfGenerator;

internal class PdfRenderer : IAsyncDisposable
{
    // Shared browser instance to avoid per-render launch/close overhead.
    // Lazily initialized, protected by a semaphore.
    private static IBrowser? _sharedBrowser;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    private const string BrowserExecutablePathEnvVar = "RPA_PDF_CHROMIUM_PATH";
    private const string DisableSandboxEnvVar = "RPA_PDF_DISABLE_SANDBOX";

    private static readonly string[] BaseChromeArgs =
    [
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--no-zygote"
    ];

    public async Task EnsureBrowserAsync(CancellationToken cancellationToken = default)
    {
        if (_sharedBrowser is { IsConnected: true }) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_sharedBrowser is { IsConnected: true }) return;

            var launchOptions = await BuildLaunchOptionsAsync(cancellationToken);
            _sharedBrowser = await Puppeteer.LaunchAsync(launchOptions).WaitAsync(cancellationToken);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task RenderAsync(
        string html,
        string outputPath,
        PaperFormat paperFormat,
        MarginOptions? margins = null,
        CancellationToken cancellationToken = default)
    {
        if (_sharedBrowser is not { IsConnected: true })
            throw new InvalidOperationException("Browser not initialized. Call EnsureBrowserAsync first.");

        cancellationToken.ThrowIfCancellationRequested();
        await using var page = await _sharedBrowser.NewPageAsync().WaitAsync(cancellationToken);
        await page.SetContentAsync(html).WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var pdfOptions = new PdfOptions
        {
            Format = paperFormat,
            PrintBackground = true,
            MarginOptions = margins ?? new MarginOptions
            {
                Top = "0.75in",
                Bottom = "0.75in",
                Left = "1in",
                Right = "1in"
            },
            DisplayHeaderFooter = true,
            HeaderTemplate = "<div/>",
            FooterTemplate =
                "<div style='font-size:9px;color:#666;width:100%;text-align:center;'>" +
                "Page <span class='pageNumber'></span> of <span class='totalPages'></span>" +
                "</div>"
        };

        await using var stream = await page.PdfStreamAsync(pdfOptions).WaitAsync(cancellationToken);
        await using var file = File.Create(outputPath);
        await stream.CopyToAsync(file, cancellationToken);
    }

    public static int CountPages(string pdfPath)
    {
        const string pageToken = "/Type /Page";
        var tokenBytes = Encoding.ASCII.GetBytes(pageToken);

        using var stream = File.OpenRead(pdfPath);
        var buffer = new byte[16 * 1024];
        var tail = Array.Empty<byte>();
        var pageCount = 0;

        while (true)
        {
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;

            var current = new byte[tail.Length + bytesRead];
            if (tail.Length > 0)
                Buffer.BlockCopy(tail, 0, current, 0, tail.Length);
            Buffer.BlockCopy(buffer, 0, current, tail.Length, bytesRead);

            var startIndex = Math.Max(0, tail.Length - tokenBytes.Length + 1);
            pageCount += CountPageTokens(current, tokenBytes, startIndex);

            var keep = Math.Min(tokenBytes.Length, current.Length);
            tail = new byte[keep];
            Buffer.BlockCopy(current, current.Length - keep, tail, 0, keep);
        }

        return pageCount > 0 ? pageCount : 1;
    }

    // The shared browser is not disposed per instance — it lives until process exit.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<LaunchOptions> BuildLaunchOptionsAsync(CancellationToken cancellationToken)
    {
        var executablePath = ResolveBrowserExecutablePath();
        if (executablePath != null)
            return CreateLaunchOptions(executablePath);

        var fetcher = new BrowserFetcher();
        try
        {
            await fetcher.DownloadAsync().WaitAsync(cancellationToken);
            return CreateLaunchOptions();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Unable to locate Chromium. Set {BrowserExecutablePathEnvVar} to a local browser binary path, " +
                "or allow Puppeteer to download Chromium.",
                ex);
        }
    }

    private static LaunchOptions CreateLaunchOptions(string? executablePath = null)
    {
        var args = IsSandboxDisabled()
            ? BaseChromeArgs.Concat(["--no-sandbox", "--disable-setuid-sandbox"]).ToArray()
            : BaseChromeArgs;

        return new LaunchOptions
        {
            Headless = true,
            Args = args,
            ExecutablePath = executablePath
        };
    }

    private static bool IsSandboxDisabled()
    {
        var value = Environment.GetEnvironmentVariable(DisableSandboxEnvVar);
        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Linux containers frequently require no-sandbox to launch Chromium.
        return OperatingSystem.IsLinux();
    }

    private static string? ResolveBrowserExecutablePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(BrowserExecutablePathEnvVar);
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var candidates = new[]
        {
            "/usr/bin/google-chrome",
            "/usr/bin/google-chrome-stable",
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser",
            "/opt/google/chrome/chrome",
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            "/Applications/Chromium.app/Contents/MacOS/Chromium",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        };

        var knownPath = candidates.FirstOrDefault(File.Exists);
        if (knownPath != null) return knownPath;

        return FindExecutableOnPath(
            OperatingSystem.IsWindows()
                ? ["chrome.exe", "msedge.exe", "chromium.exe"]
                : ["google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "msedge"]);
    }

    private static string? FindExecutableOnPath(IEnumerable<string> executableNames)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var executableName in executableNames)
            {
                var candidate = Path.Combine(folder, executableName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static int CountPageTokens(byte[] content, byte[] tokenBytes, int startIndex)
    {
        var count = 0;
        for (var i = startIndex; i <= content.Length - tokenBytes.Length; i++)
        {
            if (!MatchesAt(content, tokenBytes, i))
                continue;

            var nextIndex = i + tokenBytes.Length;
            var isPagesNode = nextIndex < content.Length && content[nextIndex] == (byte)'s';
            if (!isPagesNode)
                count++;
        }

        return count;
    }

    private static bool MatchesAt(byte[] content, byte[] tokenBytes, int offset)
    {
        for (var i = 0; i < tokenBytes.Length; i++)
        {
            if (content[offset + i] != tokenBytes[i])
                return false;
        }

        return true;
    }
}
