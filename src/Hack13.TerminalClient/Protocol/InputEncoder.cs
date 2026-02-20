using Hack13.Contracts.Protocol;

namespace Hack13.TerminalClient.Protocol;

/// <summary>
/// A field value to send back to the server.
/// </summary>
public class InputField
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Builds and sends TN5250 input data stream records to the server.
/// Encodes modified field values with AID key and cursor position.
/// </summary>
public class InputEncoder
{
    /// <summary>
    /// Builds a complete input record with GDS header, cursor position, AID key, and modified fields.
    /// </summary>
    public byte[] BuildInputRecord(byte aidKey, int cursorRow, int cursorCol, IEnumerable<InputField> fields)
    {
        ValidateAddress(cursorRow, cursorCol, nameof(cursorRow), nameof(cursorCol));

        using var body = new MemoryStream();

        // Cursor position and AID key (after GDS header)
        body.WriteByte((byte)cursorRow);
        body.WriteByte((byte)cursorCol);
        body.WriteByte(aidKey);

        // Modified fields: SBA + row + col + EBCDIC value
        foreach (var field in fields)
        {
            ValidateAddress(field.Row, field.Col, $"{nameof(InputField)}.{nameof(InputField.Row)}", $"{nameof(InputField)}.{nameof(InputField.Col)}");
            body.WriteByte(Tn5250Constants.ORDER_SBA);
            body.WriteByte((byte)field.Row);
            body.WriteByte((byte)field.Col);

            var ebcdicValue = EbcdicConverter.FromAscii(field.Value);
            body.Write(ebcdicValue, 0, ebcdicValue.Length);
        }

        var bodyBytes = body.ToArray();

        // Build GDS header (10 bytes) + body
        using var record = new MemoryStream();
        int recordLength = 10 + bodyBytes.Length;
        record.WriteByte((byte)(recordLength >> 8));
        record.WriteByte((byte)(recordLength & 0xFF));
        record.WriteByte(0x12); // Record type high
        record.WriteByte(0xA0); // Record type low
        record.WriteByte(0x04); // Variable header length high
        record.WriteByte(0x00); // Variable header length low
        record.WriteByte(0x00); // Flags
        record.WriteByte(Tn5250Constants.GDS_OPCODE_PUT_GET); // Opcode
        record.WriteByte(0x00); // Reserved
        record.WriteByte(0x00); // Reserved
        record.Write(bodyBytes, 0, bodyBytes.Length);

        var recordBytes = record.ToArray();

        // Wrap in IAC EOR framing, escaping any 0xFF
        using var framed = new MemoryStream();
        foreach (var b in recordBytes)
        {
            framed.WriteByte(b);
            if (b == Tn5250Constants.IAC)
                framed.WriteByte(Tn5250Constants.IAC);
        }
        framed.WriteByte(Tn5250Constants.IAC);
        framed.WriteByte(Tn5250Constants.EOR);

        return framed.ToArray();
    }

    /// <summary>
    /// Builds and sends an input record over the network stream.
    /// </summary>
    public async Task SendInputAsync(Stream stream, byte aidKey, int cursorRow, int cursorCol,
        IEnumerable<InputField> fields, CancellationToken ct)
    {
        var data = BuildInputRecord(aidKey, cursorRow, cursorCol, fields);
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }

    private static void ValidateAddress(int row, int col, string rowName, string colName)
    {
        if (row < 1 || row > Tn5250Constants.SCREEN_ROWS)
            throw new ArgumentOutOfRangeException(rowName, row,
                $"Row must be between 1 and {Tn5250Constants.SCREEN_ROWS}.");

        if (col < 1 || col > Tn5250Constants.SCREEN_COLS)
            throw new ArgumentOutOfRangeException(colName, col,
                $"Column must be between 1 and {Tn5250Constants.SCREEN_COLS}.");
    }
}
