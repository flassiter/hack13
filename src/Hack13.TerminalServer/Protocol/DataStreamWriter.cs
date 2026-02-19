using Hack13.Contracts.Protocol;

namespace Hack13.TerminalServer.Protocol;

/// <summary>
/// Builds 5250 data stream records for host-to-terminal communication.
/// Produces complete EOR-framed records ready to send over the wire.
/// </summary>
public class DataStreamWriter
{
    private readonly MemoryStream _body = new();

    /// <summary>
    /// Writes ESC + Clear Unit to clear the terminal screen.
    /// </summary>
    public DataStreamWriter ClearUnit()
    {
        _body.WriteByte(Tn5250Constants.ESC);
        _body.WriteByte(Tn5250Constants.CMD_CLEAR_UNIT);
        return this;
    }

    /// <summary>
    /// Writes ESC + Write to Display + control characters to start screen output.
    /// </summary>
    public DataStreamWriter WriteToDisplay(byte cc1 = 0x00, byte cc2 = 0x00)
    {
        _body.WriteByte(Tn5250Constants.ESC);
        _body.WriteByte(Tn5250Constants.CMD_WRITE_TO_DISPLAY);
        _body.WriteByte(cc1);
        _body.WriteByte(cc2);
        return this;
    }

    /// <summary>
    /// Writes SBA order to position the buffer address (1-based row/col).
    /// </summary>
    public DataStreamWriter SetBufferAddress(int row, int col)
    {
        ValidateAddress(row, col);
        _body.WriteByte(Tn5250Constants.ORDER_SBA);
        _body.WriteByte((byte)row);
        _body.WriteByte((byte)col);
        return this;
    }

    /// <summary>
    /// Writes SF order to start a field with the given FFW bytes.
    /// </summary>
    public DataStreamWriter StartField(byte ffw0, byte ffw1)
    {
        _body.WriteByte(Tn5250Constants.ORDER_SF);
        _body.WriteByte(ffw0);
        _body.WriteByte(ffw1);
        return this;
    }

    /// <summary>
    /// Writes SF for an input field (unprotected, alphanumeric).
    /// </summary>
    public DataStreamWriter StartInputField()
    {
        return StartField(Tn5250Constants.FFW_SHIFT_ALPHA, 0x00);
    }

    /// <summary>
    /// Writes SF for a non-display input field (hidden, for passwords).
    /// </summary>
    public DataStreamWriter StartHiddenField()
    {
        return StartField(Tn5250Constants.FFW_SHIFT_NONDISPLAY, 0x00);
    }

    /// <summary>
    /// Writes SF for a protected display-only field.
    /// </summary>
    public DataStreamWriter StartProtectedField()
    {
        return StartField(Tn5250Constants.FFW_BYPASS, 0x00);
    }

    /// <summary>
    /// Writes IC order to position the cursor (for initial cursor placement).
    /// </summary>
    public DataStreamWriter InsertCursor()
    {
        _body.WriteByte(Tn5250Constants.ORDER_IC);
        return this;
    }

    /// <summary>
    /// Writes RA (Repeat to Address) order to fill a region with a character.
    /// </summary>
    public DataStreamWriter RepeatToAddress(int row, int col, byte ebcdicChar)
    {
        ValidateAddress(row, col);
        _body.WriteByte(Tn5250Constants.ORDER_RA);
        _body.WriteByte((byte)row);
        _body.WriteByte((byte)col);
        _body.WriteByte(ebcdicChar);
        return this;
    }

    /// <summary>
    /// Writes EBCDIC-encoded text at the current buffer position.
    /// </summary>
    public DataStreamWriter WriteText(string asciiText)
    {
        var ebcdic = EbcdicConverter.FromAscii(asciiText);
        _body.Write(ebcdic, 0, ebcdic.Length);
        return this;
    }

    /// <summary>
    /// Writes EBCDIC spaces to pad a field to a given length.
    /// </summary>
    public DataStreamWriter WriteSpaces(int count)
    {
        for (int i = 0; i < count; i++)
            _body.WriteByte(0x40); // EBCDIC space
        return this;
    }

    /// <summary>
    /// Writes text padded/truncated to exactly the given length.
    /// </summary>
    public DataStreamWriter WriteFieldValue(string? value, int length)
    {
        var text = (value ?? "").PadRight(length);
        if (text.Length > length) text = text[..length];
        return WriteText(text);
    }

    /// <summary>
    /// Builds the complete EOR-framed record with GDS header.
    /// Uses the Put/Get opcode (send screen + invite input).
    /// </summary>
    public byte[] Build(byte opcode = Tn5250Constants.GDS_OPCODE_PUT_GET)
    {
        return BuildRecord(opcode);
    }

    /// <summary>
    /// Builds the complete EOR-framed record with GDS header using a specific opcode.
    /// </summary>
    private byte[] BuildRecord(byte opcode)
    {
        var bodyBytes = _body.ToArray();

        using var record = new MemoryStream();

        // GDS record header (10 bytes)
        int recordLength = 10 + bodyBytes.Length;
        record.WriteByte((byte)(recordLength >> 8));   // Length high byte
        record.WriteByte((byte)(recordLength & 0xFF));  // Length low byte
        record.WriteByte(0x12); // Record type high (0x12A0)
        record.WriteByte(0xA0); // Record type low
        record.WriteByte(0x04); // Variable header length high
        record.WriteByte(0x00); // Variable header length low
        record.WriteByte(0x00); // Flags
        record.WriteByte(opcode);
        record.WriteByte(0x00); // Reserved
        record.WriteByte(0x00); // Reserved

        // 5250 data stream body
        record.Write(bodyBytes, 0, bodyBytes.Length);

        var recordBytes = record.ToArray();

        // Wrap in IAC EOR framing, escaping any 0xFF in the data
        using var framed = new MemoryStream();
        foreach (var b in recordBytes)
        {
            framed.WriteByte(b);
            if (b == Tn5250Constants.IAC)
                framed.WriteByte(Tn5250Constants.IAC); // Escape 0xFF as 0xFF 0xFF
        }
        framed.WriteByte(Tn5250Constants.IAC);
        framed.WriteByte(Tn5250Constants.EOR);

        return framed.ToArray();
    }

    /// <summary>
    /// Resets the writer for building a new record.
    /// </summary>
    public void Reset()
    {
        _body.SetLength(0);
    }

    private static void ValidateAddress(int row, int col)
    {
        if (row < 1 || row > Tn5250Constants.SCREEN_ROWS)
            throw new ArgumentOutOfRangeException(nameof(row), row,
                $"Row must be between 1 and {Tn5250Constants.SCREEN_ROWS}.");

        if (col < 1 || col > Tn5250Constants.SCREEN_COLS)
            throw new ArgumentOutOfRangeException(nameof(col), col,
                $"Column must be between 1 and {Tn5250Constants.SCREEN_COLS}.");
    }
}
