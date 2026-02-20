using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Hack13.Contracts.Protocol;
using Hack13.TerminalClient.Protocol;

namespace Hack13.TerminalClient;

/// <summary>
/// Standalone helper that connects to a TN5250 endpoint (optionally with TLS),
/// performs telnet negotiation, reads the initial screen, and dumps it.
/// Useful for verifying connectivity to a real iSeries.
/// </summary>
public static class TlsConnectionTest
{
    public static async Task<int> RunAsync(
        string host,
        int port,
        bool useTls,
        string? caCertPath,
        bool insecureSkipVerify,
        string terminalType = "IBM-3179-2",
        string? deviceName = null,
        int timeoutSeconds = 15)
    {
        Console.WriteLine($"Connecting to {host}:{port} (TLS={useTls})...");

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

            await client.ConnectAsync(host, port, cts.Token);
            Console.WriteLine("TCP connected.");

            Stream stream = client.GetStream();
            SslStream? sslStream = null;

            if (useTls)
            {
                Console.WriteLine("Starting TLS handshake...");
                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                };

                if (insecureSkipVerify)
                {
                    sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                    Console.WriteLine("  (certificate validation disabled)");
                }
                else if (!string.IsNullOrWhiteSpace(caCertPath))
                {
                    var caCert = new X509Certificate2(caCertPath);
                    var caCollection = new X509Certificate2Collection { caCert };
                    Console.WriteLine($"  Trusting CA: {caCert.Subject}");

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
                await sslStream.AuthenticateAsClientAsync(sslOptions, cts.Token);
                stream = sslStream;

                Console.WriteLine($"TLS established: {sslStream.SslProtocol}, cipher={sslStream.CipherAlgorithm}");
                if (sslStream.RemoteCertificate is { } remoteCert)
                    Console.WriteLine($"Server cert: {remoteCert.Subject}");
            }

            try
            {
                // Telnet negotiation
                Console.WriteLine("Starting telnet negotiation...");
                var negotiator = new ClientTelnetNegotiator(
                    stream,
                    msg => Console.WriteLine($"  [negotiation] {msg}"),
                    terminalType,
                    deviceName);
                await negotiator.NegotiateAsync(cts.Token);
                Console.WriteLine("Telnet negotiation complete.");

                // Read initial screen
                Console.WriteLine("Reading initial screen...");
                var parser = new DataStreamParser(msg => { });
                var buffer = new ScreenBuffer();
                await parser.ReadAndParseScreenAsync(stream, buffer, negotiator.ConsumePendingData(), cts.Token);

                // Dump the screen
                Console.WriteLine();
                Console.WriteLine(new string('=', 82));
                for (int row = 1; row <= buffer.Rows; row++)
                {
                    Console.Write('|');
                    Console.Write(buffer.ReadRow(row));
                    Console.WriteLine('|');
                }
                Console.WriteLine(new string('=', 82));
                Console.WriteLine();
                Console.WriteLine($"Cursor position: row={buffer.CursorRow}, col={buffer.CursorCol}");
                Console.WriteLine($"Fields detected: {buffer.Fields.Count} ({buffer.GetInputFields().Count()} input)");

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Connection test PASSED.");
                Console.ResetColor();
                return 0;
            }
            finally
            {
                if (sslStream != null) await sslStream.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Connection test FAILED: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"  Inner: {ex.InnerException.Message}");
            Console.ResetColor();
            return 1;
        }
    }
}
