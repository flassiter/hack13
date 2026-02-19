using System.Net;
using System.Net.Sockets;
using Hack13.Contracts.Protocol;
using Hack13.TerminalClient.Protocol;
using Hack13.TerminalServer.Protocol;
using MockWriter = Hack13.TerminalServer.Protocol.DataStreamWriter;
using MockConstants = Hack13.Contracts.Protocol.Tn5250Constants;

namespace Hack13.TerminalClient.Tests;

public class DataStreamParserTests
{
    private readonly DataStreamParser _parser = new(_ => { });

    [Fact]
    public void ParseRecord_handles_clear_unit()
    {
        var writer = new MockWriter();
        writer.ClearUnit();
        var data = writer.Build(MockConstants.GDS_OPCODE_PUT_GET);
        var raw = StripFraming(data);

        var buf = new ScreenBuffer();
        buf.SetChar(1, 1, 'X'); // set something first
        _parser.ParseRecord(raw, buf);

        Assert.Equal(' ', buf.GetChar(1, 1)); // cleared
    }

    [Fact]
    public void ParseRecord_handles_static_text()
    {
        var writer = new MockWriter();
        writer.ClearUnit();
        writer.WriteToDisplay();
        writer.SetBufferAddress(1, 28);
        writer.WriteText("Sign On");
        var data = writer.Build(MockConstants.GDS_OPCODE_PUT_GET);
        var raw = StripFraming(data);

        var buf = new ScreenBuffer();
        _parser.ParseRecord(raw, buf);

        Assert.Equal("Sign On", buf.ReadText(1, 28, 7));
    }

    [Fact]
    public void ParseRecord_handles_input_field()
    {
        var writer = new MockWriter();
        writer.ClearUnit();
        writer.WriteToDisplay();
        writer.SetBufferAddress(10, 35);
        writer.StartInputField(); // SF at (10, 35)
        writer.WriteFieldValue("TESTUSER", 10);
        writer.StartProtectedField(); // end-of-field marker
        var data = writer.Build(MockConstants.GDS_OPCODE_PUT_GET);
        var raw = StripFraming(data);

        var buf = new ScreenBuffer();
        _parser.ParseRecord(raw, buf);

        // Field data starts at col 36 (col 35 is the SF attribute byte)
        Assert.Equal("TESTUSER  ", buf.ReadText(10, 36, 10));

        // Should have parsed the input field
        var fields = buf.Fields;
        Assert.True(fields.Count >= 1);
        var inputField = fields.FirstOrDefault(f => f.Row == 10 && f.Col == 35 && f.IsInput);
        Assert.NotNull(inputField);
    }

    [Fact]
    public void ParseRecord_handles_protected_display_field()
    {
        var writer = new MockWriter();
        writer.ClearUnit();
        writer.WriteToDisplay();
        writer.SetBufferAddress(5, 35);
        writer.StartProtectedField(); // SF bypass at (5, 35)
        writer.WriteFieldValue("$198,543.21", 15);
        var data = writer.Build(MockConstants.GDS_OPCODE_PUT_GET);
        var raw = StripFraming(data);

        var buf = new ScreenBuffer();
        _parser.ParseRecord(raw, buf);

        var text = buf.ReadText(5, 36, 15).TrimEnd();
        Assert.Equal("$198,543.21", text);

        var field = buf.Fields.FirstOrDefault(f => f.Row == 5 && f.Col == 35);
        Assert.NotNull(field);
        Assert.True(field.IsProtected);
    }

    [Fact]
    public void ParseRecord_handles_cursor_position()
    {
        var writer = new MockWriter();
        writer.ClearUnit();
        writer.WriteToDisplay();
        writer.SetBufferAddress(10, 36);
        writer.InsertCursor();
        var data = writer.Build(MockConstants.GDS_OPCODE_PUT_GET);
        var raw = StripFraming(data);

        var buf = new ScreenBuffer();
        _parser.ParseRecord(raw, buf);

        Assert.Equal(10, buf.CursorRow);
        Assert.Equal(36, buf.CursorCol);
    }

    [Fact]
    public void ParseRecord_handles_full_sign_on_screen()
    {
        // Build a realistic sign-on screen matching the mock server's rendering
        var writer = new MockWriter();
        writer.ClearUnit();
        writer.WriteToDisplay(MockConstants.CC1_LOCK_KEYBOARD, MockConstants.CC2_NO_FLAGS);

        // Static text
        writer.SetBufferAddress(1, 28);
        writer.WriteText("Sign On");
        writer.SetBufferAddress(3, 20);
        writer.WriteText("Mortgage Servicing System");

        // User ID input field
        writer.SetBufferAddress(10, 17);
        writer.WriteText("User ID . . . . :");
        writer.SetBufferAddress(10, 35);
        writer.StartInputField();
        writer.WriteFieldValue("", 10);
        writer.StartProtectedField();

        // Password input field
        writer.SetBufferAddress(12, 17);
        writer.WriteText("Password  . . . :");
        writer.SetBufferAddress(12, 35);
        writer.StartHiddenField();
        writer.WriteFieldValue("", 10);
        writer.StartProtectedField();

        // Cursor at first input field
        writer.SetBufferAddress(10, 36);
        writer.InsertCursor();

        var data = writer.Build(MockConstants.GDS_OPCODE_PUT_GET);
        var raw = StripFraming(data);

        var buf = new ScreenBuffer();
        _parser.ParseRecord(raw, buf);

        // Verify text
        Assert.Equal("Sign On", buf.ReadText(1, 28, 7));
        Assert.Equal("Mortgage Servicing System", buf.ReadText(3, 20, 25));

        // Verify input fields exist
        var inputFields = buf.GetInputFields().ToList();
        Assert.True(inputFields.Count >= 2);

        // Verify cursor
        Assert.Equal(10, buf.CursorRow);
        Assert.Equal(36, buf.CursorCol);
    }

    [Fact]
    public void ParseRecord_handles_repeat_to_address()
    {
        var writer = new MockWriter();
        writer.ClearUnit();
        writer.WriteToDisplay();
        writer.SetBufferAddress(1, 1);
        writer.RepeatToAddress(1, 80, Hack13.Contracts.Protocol.EbcdicConverter.FromAscii((byte)'-'));
        var data = writer.Build(MockConstants.GDS_OPCODE_PUT_GET);
        var raw = StripFraming(data);

        var buf = new ScreenBuffer();
        _parser.ParseRecord(raw, buf);

        // Should have dashes from col 1 to col 79 (RA fills up to but not including target)
        for (int c = 1; c < 80; c++)
            Assert.Equal('-', buf.GetChar(1, c));
    }

    [Fact]
    public void ParseRecord_throws_for_unsupported_variable_width_order()
    {
        // Minimal GDS header + unsupported ORDER_TD.
        var raw = new byte[] { 0x00, 0x0B, 0x12, 0xA0, 0x04, 0x00, 0x00, 0x03, 0x00, 0x00, MockConstants.ORDER_TD };
        var buf = new ScreenBuffer();
        Assert.Throws<InvalidDataException>(() => _parser.ParseRecord(raw, buf));
    }

    [Fact]
    public async Task ReadEorFrameAsync_ignores_inband_telnet_option_commands()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var server = await listener.AcceptTcpClientAsync();
        await connectTask;

        using var clientStream = client.GetStream();
        using var serverStream = server.GetStream();

        // Send payload bytes with an in-band IAC DO BINARY command and EOR terminator.
        var frame = new byte[]
        {
            0x12, 0xA0,
            MockConstants.IAC, MockConstants.DO, MockConstants.OPT_BINARY,
            0x34,
            MockConstants.IAC, MockConstants.EOR
        };
        await serverStream.WriteAsync(frame);
        await serverStream.FlushAsync();

        var result = await _parser.ReadEorFrameAsync(clientStream, CancellationToken.None);
        Assert.Equal(new byte[] { 0x12, 0xA0, 0x34 }, result);
    }

    private static byte[] StripFraming(byte[] framed)
    {
        using var ms = new MemoryStream();
        for (int i = 0; i < framed.Length - 2; i++)
        {
            if (framed[i] == 0xFF && i + 1 < framed.Length - 2 && framed[i + 1] == 0xFF)
            {
                ms.WriteByte(0xFF);
                i++;
            }
            else
            {
                ms.WriteByte(framed[i]);
            }
        }
        return ms.ToArray();
    }
}
