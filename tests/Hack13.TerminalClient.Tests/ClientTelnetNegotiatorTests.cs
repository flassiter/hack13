using System.Net;
using System.Net.Sockets;
using Hack13.Contracts.Protocol;
using Hack13.TerminalClient.Protocol;

namespace Hack13.TerminalClient.Tests;

public class ClientTelnetNegotiatorTests
{
    [Fact]
    public async Task NegotiateAsync_refuses_unsupported_options()
    {
        var (client, server) = await CreateConnectedPairAsync();
        using var _ = client;
        using var __ = server;

        using var clientStream = client.GetStream();
        using var serverStream = server.GetStream();
        var negotiator = new ClientTelnetNegotiator(clientStream, _ => { });

        var negotiateTask = negotiator.NegotiateAsync(CancellationToken.None);

        await serverStream.WriteAsync(new byte[]
        {
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_NEW_ENVIRON,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_TERMINAL_TYPE,
            Tn5250Constants.IAC, Tn5250Constants.SB, Tn5250Constants.OPT_TERMINAL_TYPE, Tn5250Constants.TERMINAL_TYPE_SEND,
            Tn5250Constants.IAC, Tn5250Constants.SE,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_BINARY,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_BINARY
        });
        await serverStream.FlushAsync();

        await negotiateTask;

        var response = await ReadAvailableAsync(serverStream);
        AssertContains(response, new byte[] { Tn5250Constants.IAC, Tn5250Constants.WONT, Tn5250Constants.OPT_NEW_ENVIRON });
        AssertContains(response, new byte[] { Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_TERMINAL_TYPE });
        AssertContains(response, new byte[] { Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_BINARY });
    }

    [Fact]
    public async Task NegotiateAsync_preserves_non_telnet_data_read_during_negotiation()
    {
        var (client, server) = await CreateConnectedPairAsync();
        using var _ = client;
        using var __ = server;

        using var clientStream = client.GetStream();
        using var serverStream = server.GetStream();
        var negotiator = new ClientTelnetNegotiator(clientStream, _ => { });

        var negotiateTask = negotiator.NegotiateAsync(CancellationToken.None);

        await serverStream.WriteAsync(new byte[]
        {
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_TERMINAL_TYPE,
            Tn5250Constants.IAC, Tn5250Constants.SB, Tn5250Constants.OPT_TERMINAL_TYPE, Tn5250Constants.TERMINAL_TYPE_SEND,
            Tn5250Constants.IAC, Tn5250Constants.SE,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_BINARY,
            0x44,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_BINARY
        });
        await serverStream.FlushAsync();

        await negotiateTask;
        Assert.Equal(new byte[] { 0x44 }, negotiator.ConsumePendingData());
    }

    [Fact]
    public async Task NegotiateAsync_sends_terminal_type_with_device_name_when_configured()
    {
        var (client, server) = await CreateConnectedPairAsync();
        using var _ = client;
        using var __ = server;

        using var clientStream = client.GetStream();
        using var serverStream = server.GetStream();
        var negotiator = new ClientTelnetNegotiator(
            clientStream,
            _ => { },
            terminalType: "IBM-3179-2",
            deviceName: "RPADEV01");

        var negotiateTask = negotiator.NegotiateAsync(CancellationToken.None);

        await serverStream.WriteAsync(new byte[]
        {
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_TERMINAL_TYPE,
            Tn5250Constants.IAC, Tn5250Constants.SB, Tn5250Constants.OPT_TERMINAL_TYPE, Tn5250Constants.TERMINAL_TYPE_SEND,
            Tn5250Constants.IAC, Tn5250Constants.SE,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_BINARY,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_BINARY
        });
        await serverStream.FlushAsync();

        await negotiateTask;

        var response = await ReadAvailableAsync(serverStream);
        var responseAscii = System.Text.Encoding.ASCII.GetString(response);
        Assert.Contains("IBM-3179-2@RPADEV01", responseAscii, StringComparison.Ordinal);
    }

    private static async Task<(TcpClient client, TcpClient server)> CreateConnectedPairAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        var server = await listener.AcceptTcpClientAsync();
        await connectTask;
        return (client, server);
    }

    private static async Task<byte[]> ReadAvailableAsync(NetworkStream stream)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var buffer = new byte[1024];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
        return buffer[..bytesRead];
    }

    private static void AssertContains(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return;
        }

        Assert.Fail($"Sequence not found: {BitConverter.ToString(needle)} in {BitConverter.ToString(haystack)}");
    }
}
