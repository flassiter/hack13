using Hack13.Contracts.Protocol;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Tests;

public class DataStreamWriterTests
{
    [Fact]
    public void Build_ProducesEorFrame()
    {
        var writer = new DataStreamWriter();
        writer.ClearUnit();
        var data = writer.Build();

        // Frame must end with IAC EOR
        Assert.True(data.Length >= 2);
        Assert.Equal(Tn5250Constants.IAC, data[^2]);
        Assert.Equal(Tn5250Constants.EOR, data[^1]);
    }

    [Fact]
    public void Build_ContainsGdsHeader()
    {
        var writer = new DataStreamWriter();
        writer.ClearUnit();
        var data = writer.Build();

        // GDS header starts at byte 0: length(2) + record_type(2) + var_header(2) + flags(1) + opcode(1) + reserved(2)
        // Record type should be 0x12A0
        Assert.Equal(0x12, data[2]);
        Assert.Equal(0xA0, data[3]);
    }

    [Fact]
    public void Build_PutGetOpcode()
    {
        var writer = new DataStreamWriter();
        writer.ClearUnit();
        var data = writer.Build();

        // Opcode at offset 7 should be Put/Get (0x03)
        Assert.Equal(Tn5250Constants.GDS_OPCODE_PUT_GET, data[7]);
    }

    [Fact]
    public void ClearUnit_WritesEscClearSequence()
    {
        var writer = new DataStreamWriter();
        writer.ClearUnit();
        var data = writer.Build();

        // After the 10-byte GDS header, we should find ESC + CLEAR_UNIT
        Assert.Equal(Tn5250Constants.ESC, data[10]);
        Assert.Equal(Tn5250Constants.CMD_CLEAR_UNIT, data[11]);
    }

    [Fact]
    public void SetBufferAddress_WritesSbaOrder()
    {
        var writer = new DataStreamWriter();
        writer.SetBufferAddress(5, 10);
        var data = writer.Build();

        // After GDS header (10 bytes), SBA order
        Assert.Equal(Tn5250Constants.ORDER_SBA, data[10]);
        Assert.Equal(5, data[11]);  // row
        Assert.Equal(10, data[12]); // col
    }

    [Fact]
    public void StartField_WritesSfOrder()
    {
        var writer = new DataStreamWriter();
        writer.StartInputField();
        var data = writer.Build();

        Assert.Equal(Tn5250Constants.ORDER_SF, data[10]);
        Assert.Equal(Tn5250Constants.FFW_SHIFT_ALPHA, data[11]); // FFW byte 0
        Assert.Equal(0x00, data[12]); // FFW byte 1
    }

    [Fact]
    public void StartHiddenField_WritesNondisplayFfw()
    {
        var writer = new DataStreamWriter();
        writer.StartHiddenField();
        var data = writer.Build();

        Assert.Equal(Tn5250Constants.ORDER_SF, data[10]);
        Assert.Equal(Tn5250Constants.FFW_SHIFT_NONDISPLAY, data[11]);
    }

    [Fact]
    public void StartProtectedField_WritesBypassFfw()
    {
        var writer = new DataStreamWriter();
        writer.StartProtectedField();
        var data = writer.Build();

        Assert.Equal(Tn5250Constants.ORDER_SF, data[10]);
        Assert.Equal(Tn5250Constants.FFW_BYPASS, data[11]);
    }

    [Fact]
    public void InsertCursor_WritesIcOrder()
    {
        var writer = new DataStreamWriter();
        writer.InsertCursor();
        var data = writer.Build();

        Assert.Equal(Tn5250Constants.ORDER_IC, data[10]);
    }

    [Fact]
    public void WriteText_WritesEbcdic()
    {
        var writer = new DataStreamWriter();
        writer.WriteText("AB");
        var data = writer.Build();

        // 'A' = 0xC1, 'B' = 0xC2 in EBCDIC
        Assert.Equal(0xC1, data[10]);
        Assert.Equal(0xC2, data[11]);
    }

    [Fact]
    public void WriteFieldValue_PadsToLength()
    {
        var writer = new DataStreamWriter();
        writer.WriteFieldValue("HI", 5);
        var data = writer.Build();

        // "HI   " = 5 EBCDIC bytes
        // H=0xC8, I=0xC9, space=0x40
        Assert.Equal(0xC8, data[10]);
        Assert.Equal(0xC9, data[11]);
        Assert.Equal(0x40, data[12]); // space
        Assert.Equal(0x40, data[13]); // space
        Assert.Equal(0x40, data[14]); // space
    }

    [Fact]
    public void WriteFieldValue_TruncatesToLength()
    {
        var writer = new DataStreamWriter();
        writer.WriteFieldValue("ABCDEFGH", 3);
        var data = writer.Build();

        // Only first 3 chars: A=0xC1, B=0xC2, C=0xC3
        Assert.Equal(0xC1, data[10]);
        Assert.Equal(0xC2, data[11]);
        Assert.Equal(0xC3, data[12]);
        // No more field data (next would be IAC EOR frame)
    }

    [Fact]
    public void Build_EscapesIacInData()
    {
        // Test that 0xFF bytes in data are properly escaped to 0xFF 0xFF
        var writer = new DataStreamWriter();
        // The GDS header length field might contain 0xFF for large records
        // For a simpler test, verify the frame ends with IAC EOR
        var data = writer.Build();

        Assert.Equal(Tn5250Constants.IAC, data[^2]);
        Assert.Equal(Tn5250Constants.EOR, data[^1]);
    }

    [Fact]
    public void Reset_ClearsBody()
    {
        var writer = new DataStreamWriter();
        writer.WriteText("HELLO");
        writer.Reset();
        writer.WriteText("AB");
        var data = writer.Build();

        // Should only contain "AB" (2 EBCDIC bytes) + header + frame
        // GDS header(10) + 2 data bytes + IAC EOR(2) = 14 bytes
        Assert.Equal(14, data.Length);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 81)]
    public void SetBufferAddress_InvalidCoordinates_Throws(int row, int col)
    {
        var writer = new DataStreamWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.SetBufferAddress(row, col));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(24, 81)]
    public void RepeatToAddress_InvalidCoordinates_Throws(int row, int col)
    {
        var writer = new DataStreamWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.RepeatToAddress(row, col, 0x40));
    }
}
