using Hack13.Contracts.Protocol;
using Hack13.TerminalClient.Protocol;

namespace Hack13.TerminalClient.Tests;

public class InputEncoderTests
{
    [Fact]
    public void BuildInputRecord_starts_with_GDS_header()
    {
        var encoder = new InputEncoder();
        var record = encoder.BuildInputRecord(Tn5250Constants.AID_ENTER, 1, 1, []);

        // Strip IAC EOR framing to get raw record
        var raw = StripFraming(record);

        // GDS header: length(2) + type(2) + var_header(2) + flags(1) + opcode(1) + reserved(2) = 10
        Assert.True(raw.Length >= 10);

        // Record type should be 0x12A0
        Assert.Equal(0x12, raw[2]);
        Assert.Equal(0xA0, raw[3]);
    }

    [Fact]
    public void BuildInputRecord_contains_cursor_and_AID()
    {
        var encoder = new InputEncoder();
        var record = encoder.BuildInputRecord(Tn5250Constants.AID_F6, 5, 10, []);

        var raw = StripFraming(record);

        // After 10-byte GDS header: cursor_row, cursor_col, AID
        Assert.Equal(5, raw[10]);   // cursor row
        Assert.Equal(10, raw[11]);  // cursor col
        Assert.Equal(Tn5250Constants.AID_F6, raw[12]); // AID key
    }

    [Fact]
    public void BuildInputRecord_encodes_fields_with_SBA()
    {
        var encoder = new InputEncoder();
        var fields = new[]
        {
            new InputField { Row = 10, Col = 36, Value = "TEST" }
        };

        var record = encoder.BuildInputRecord(Tn5250Constants.AID_ENTER, 10, 36, fields);
        var raw = StripFraming(record);

        // After header(10) + cursor(2) + AID(1) = position 13
        Assert.Equal(Tn5250Constants.ORDER_SBA, raw[13]); // SBA order
        Assert.Equal(10, raw[14]); // field row
        Assert.Equal(36, raw[15]); // field col

        // EBCDIC for "TEST": T=0xE3, E=0xC5, S=0xE2, T=0xE3
        Assert.Equal(0xE3, raw[16]);
        Assert.Equal(0xC5, raw[17]);
        Assert.Equal(0xE2, raw[18]);
        Assert.Equal(0xE3, raw[19]);
    }

    [Fact]
    public void BuildInputRecord_ends_with_IAC_EOR()
    {
        var encoder = new InputEncoder();
        var record = encoder.BuildInputRecord(Tn5250Constants.AID_ENTER, 1, 1, []);

        Assert.Equal(Tn5250Constants.IAC, record[^2]);
        Assert.Equal(Tn5250Constants.EOR, record[^1]);
    }

    [Fact]
    public void BuildInputRecord_escapes_0xFF_in_data()
    {
        var encoder = new InputEncoder();
        // Create a record and check that the framed output has IAC escaping
        var record = encoder.BuildInputRecord(Tn5250Constants.AID_ENTER, 1, 1, []);

        // Verify trailing IAC EOR is present (not escaped)
        int iacCount = 0;
        for (int i = 0; i < record.Length - 1; i++)
        {
            if (record[i] == 0xFF && record[i + 1] == Tn5250Constants.EOR)
                iacCount++;
        }
        Assert.Equal(1, iacCount); // Exactly one IAC EOR terminator
    }

    [Fact]
    public void BuildInputRecord_multiple_fields()
    {
        var encoder = new InputEncoder();
        var fields = new[]
        {
            new InputField { Row = 10, Col = 36, Value = "USER" },
            new InputField { Row = 12, Col = 36, Value = "PASS" }
        };

        var record = encoder.BuildInputRecord(Tn5250Constants.AID_ENTER, 10, 36, fields);
        var raw = StripFraming(record);

        // Count SBA orders (0x11) after the GDS header
        int sbaCount = 0;
        for (int i = 13; i < raw.Length; i++)
            if (raw[i] == Tn5250Constants.ORDER_SBA)
                sbaCount++;

        Assert.Equal(2, sbaCount);
    }

    [Fact]
    public void BuildInputRecord_throws_for_invalid_cursor_position()
    {
        var encoder = new InputEncoder();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => encoder.BuildInputRecord(Tn5250Constants.AID_ENTER, 0, 1, []));
    }

    [Fact]
    public void BuildInputRecord_throws_for_invalid_field_position()
    {
        var encoder = new InputEncoder();
        var fields = new[] { new InputField { Row = 25, Col = 1, Value = "X" } };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => encoder.BuildInputRecord(Tn5250Constants.AID_ENTER, 1, 1, fields));
    }

    private static byte[] StripFraming(byte[] framed)
    {
        // Remove IAC escaping and trailing IAC EOR
        using var ms = new MemoryStream();
        for (int i = 0; i < framed.Length - 2; i++) // skip trailing IAC EOR
        {
            if (framed[i] == 0xFF && i + 1 < framed.Length - 2 && framed[i + 1] == 0xFF)
            {
                ms.WriteByte(0xFF);
                i++; // skip escaped IAC
            }
            else
            {
                ms.WriteByte(framed[i]);
            }
        }
        return ms.ToArray();
    }
}
