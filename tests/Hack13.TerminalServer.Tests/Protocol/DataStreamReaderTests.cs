using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Hack13.Contracts.Protocol;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Tests.Protocol;

public class DataStreamReaderTests
{
    [Fact]
    public async Task ReadInputAsync_IgnoresInBandTelnetCommands()
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

        var record = BuildMinimalInputRecord(Tn5250Constants.AID_ENTER);
        await clientStream.WriteAsync(record);

        // Inject a telnet command in the data phase before EOR.
        await clientStream.WriteAsync(new byte[] { Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_BINARY });
        await clientStream.WriteAsync(new byte[] { Tn5250Constants.IAC, Tn5250Constants.EOR });
        await clientStream.FlushAsync();

        var reader = new DataStreamReader(serverStream, NullLogger.Instance, TimeSpan.FromSeconds(1));
        var input = await reader.ReadInputAsync(CancellationToken.None);

        Assert.Equal(Tn5250Constants.AID_ENTER, input.AidKey);
        Assert.Equal(1, input.CursorRow);
        Assert.Equal(1, input.CursorCol);
    }

    [Fact]
    public async Task ReadInputAsync_TimesOutWhenNoDataArrives()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var serverSocket = await listener.AcceptTcpClientAsync();
        await connectTask;

        using var serverStream = serverSocket.GetStream();

        var reader = new DataStreamReader(serverStream, NullLogger.Instance, TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<TimeoutException>(() => reader.ReadInputAsync(CancellationToken.None));
    }

    private static byte[] BuildMinimalInputRecord(byte aidKey)
    {
        return
        [
            0x00, 0x0D, // GDS length = 13
            0x12, 0xA0, // GDS record type
            0x04, 0x00, // var header length
            0x00,       // flags
            0x03,       // opcode put/get
            0x00, 0x00, // reserved
            0x01,       // cursor row
            0x01,       // cursor col
            aidKey
        ];
    }
}
