using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Hack13.Contracts.Protocol;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Tests.Protocol;

public class TelnetNegotiatorTests
{
    [Fact]
    public async Task NegotiateAsync_ParsesTerminalTypeAndCompletes()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var serverSocket = await listener.AcceptTcpClientAsync();
        await connectTask;

        using var serverStream = serverSocket.GetStream();
        using var clientStream = client.GetStream();

        var negotiationTask = Task.Run(async () =>
        {
            var negotiator = new TelnetNegotiator(serverStream, NullLogger.Instance, TimeSpan.FromSeconds(1));
            await negotiator.NegotiateAsync(CancellationToken.None);
            return negotiator.TerminalType;
        });

        // Send full response stream from terminal side (order/bundling can vary in practice).
        var response = new List<byte>
        {
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_TERMINAL_TYPE,
            Tn5250Constants.IAC, Tn5250Constants.SB, Tn5250Constants.OPT_TERMINAL_TYPE, Tn5250Constants.TERMINAL_TYPE_IS
        };
        response.AddRange(System.Text.Encoding.ASCII.GetBytes("IBM-3477-FC"));
        response.AddRange(
        [
            Tn5250Constants.IAC, Tn5250Constants.SE,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_BINARY,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_BINARY
        ]);

        await clientStream.WriteAsync(response.ToArray());
        await clientStream.FlushAsync();

        var terminalType = await negotiationTask;
        Assert.Equal("IBM-3477-FC", terminalType);
    }

    [Fact]
    public async Task NegotiateAsync_ParsesDeviceNameFromTerminalTypeDeclaration()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var serverSocket = await listener.AcceptTcpClientAsync();
        await connectTask;

        using var serverStream = serverSocket.GetStream();
        using var clientStream = client.GetStream();

        var negotiationTask = Task.Run(async () =>
        {
            var negotiator = new TelnetNegotiator(serverStream, NullLogger.Instance, TimeSpan.FromSeconds(1));
            await negotiator.NegotiateAsync(CancellationToken.None);
            return (negotiator.TerminalType, negotiator.DeviceName);
        });

        var response = new List<byte>
        {
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_TERMINAL_TYPE,
            Tn5250Constants.IAC, Tn5250Constants.SB, Tn5250Constants.OPT_TERMINAL_TYPE, Tn5250Constants.TERMINAL_TYPE_IS
        };
        response.AddRange(System.Text.Encoding.ASCII.GetBytes("IBM-3477-FC@MOCKDEV1"));
        response.AddRange(
        [
            Tn5250Constants.IAC, Tn5250Constants.SE,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_BINARY,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_BINARY
        ]);

        await clientStream.WriteAsync(response.ToArray());
        await clientStream.FlushAsync();

        var (terminalType, deviceName) = await negotiationTask;
        Assert.Equal("IBM-3477-FC", terminalType);
        Assert.Equal("MOCKDEV1", deviceName);
    }

    [Fact]
    public async Task NegotiateAsync_ThrowsWhenRequiredOptionRefused()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var serverSocket = await listener.AcceptTcpClientAsync();
        await connectTask;

        using var serverStream = serverSocket.GetStream();
        using var clientStream = client.GetStream();

        var negotiationTask = Task.Run(async () =>
        {
            var negotiator = new TelnetNegotiator(serverStream, NullLogger.Instance, TimeSpan.FromSeconds(1));
            await negotiator.NegotiateAsync(CancellationToken.None);
        });

        // Accept terminal type + EOR, refuse BINARY.
        var response = new List<byte>
        {
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_TERMINAL_TYPE,
            Tn5250Constants.IAC, Tn5250Constants.SB, Tn5250Constants.OPT_TERMINAL_TYPE, Tn5250Constants.TERMINAL_TYPE_IS
        };
        response.AddRange(System.Text.Encoding.ASCII.GetBytes("IBM-3477-FC"));
        response.AddRange(
        [
            Tn5250Constants.IAC, Tn5250Constants.SE,
            Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_END_OF_RECORD,
            Tn5250Constants.IAC, Tn5250Constants.WONT, Tn5250Constants.OPT_BINARY
        ]);

        await clientStream.WriteAsync(response.ToArray());
        await clientStream.FlushAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => negotiationTask);
    }
}
