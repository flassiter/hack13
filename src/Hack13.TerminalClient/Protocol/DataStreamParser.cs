using Hack13.Contracts.Protocol;

namespace Hack13.TerminalClient.Protocol;

/// <summary>
/// Parses incoming TN5250 data stream records from the server and populates a ScreenBuffer.
/// Handles EOR framing, GDS header, and 5250 Write to Display commands/orders.
/// </summary>
public class DataStreamParser
{
    private readonly Action<string> _log;

    public DataStreamParser(Action<string> log)
    {
        _log = log;
    }

    /// <summary>
    /// Reads one complete EOR-delimited record from the stream and parses it into the screen buffer.
    /// </summary>
    public async Task<ScreenBuffer> ReadAndParseScreenAsync(Stream stream, ScreenBuffer buffer, CancellationToken ct)
    {
        var data = await ReadEorFrameAsync(stream, null, ct);
        ParseRecord(data, buffer);
        return buffer;
    }

    /// <summary>
    /// Reads one complete EOR-delimited record from the stream, including any pre-read bytes.
    /// </summary>
    public async Task<ScreenBuffer> ReadAndParseScreenAsync(
        Stream stream,
        ScreenBuffer buffer,
        byte[]? initialData,
        CancellationToken ct)
    {
        var data = await ReadEorFrameAsync(stream, initialData, ct);
        ParseRecord(data, buffer);
        return buffer;
    }

    /// <summary>
    /// Reads bytes from the stream until IAC EOR is encountered.
    /// Handles IAC IAC escaping (0xFF 0xFF -> single 0xFF).
    /// </summary>
    public async Task<byte[]> ReadEorFrameAsync(Stream stream, CancellationToken ct)
    {
        return await ReadEorFrameAsync(stream, null, ct);
    }

    public async Task<byte[]> ReadEorFrameAsync(Stream stream, byte[]? initialData, CancellationToken ct)
    {
        using var frameBuffer = new MemoryStream();
        var readBuf = new byte[1024];
        int bufPos = 0;
        int bufLen = 0;
        int initialPos = 0;

        async Task<byte> ReadNextByteAsync()
        {
            if (initialData != null && initialPos < initialData.Length)
                return initialData[initialPos++];

            if (bufPos >= bufLen)
            {
                bufLen = await stream.ReadAsync(readBuf.AsMemory(0, readBuf.Length), ct);
                if (bufLen == 0) throw new IOException("Server disconnected");
                bufPos = 0;
            }

            return readBuf[bufPos++];
        }

        while (true)
        {
            byte b = await ReadNextByteAsync();

            if (b == Tn5250Constants.IAC)
            {
                byte command = await ReadNextByteAsync();

                if (command == Tn5250Constants.EOR)
                    return frameBuffer.ToArray();

                if (command == Tn5250Constants.IAC)
                {
                    frameBuffer.WriteByte(Tn5250Constants.IAC);
                    continue;
                }

                if (command is Tn5250Constants.DO or Tn5250Constants.DONT
                    or Tn5250Constants.WILL or Tn5250Constants.WONT)
                {
                    _ = await ReadNextByteAsync(); // option byte
                    continue;
                }

                if (command == Tn5250Constants.SB)
                {
                    bool lastWasIac = false;
                    while (true)
                    {
                        var sb = await ReadNextByteAsync();
                        if (lastWasIac && sb == Tn5250Constants.SE)
                            break;
                        lastWasIac = sb == Tn5250Constants.IAC;
                    }
                    continue;
                }

                continue;
            }

            frameBuffer.WriteByte(b);
        }
    }

    /// <summary>
    /// Parses a raw 5250 output record (after GDS header) into the screen buffer.
    /// Handles Clear Unit, Write to Display, SBA, SF, RA, IC orders and text data.
    /// </summary>
    public void ParseRecord(byte[] data, ScreenBuffer buffer)
    {
        if (data.Length < 10)
        {
            _log($"Record too short ({data.Length} bytes), skipping");
            return;
        }

        // Skip GDS header (10 bytes)
        int pos = 10;
        int currentRow = 1;
        int currentCol = 1;

        // Track field starts to compute field lengths
        var fieldStarts = new List<(int row, int col, byte ffw0, byte ffw1)>();

        while (pos < data.Length)
        {
            byte b = data[pos];

            if (b == Tn5250Constants.ESC && pos + 1 < data.Length)
            {
                byte cmd = data[pos + 1];
                pos += 2;

                if (cmd == Tn5250Constants.CMD_CLEAR_UNIT)
                {
                    buffer.Clear();
                    currentRow = 1;
                    currentCol = 1;
                }
                else if (cmd == Tn5250Constants.CMD_WRITE_TO_DISPLAY)
                {
                    // Skip CC1 and CC2 control characters
                    pos += 2;
                }
            }
            else if (b == Tn5250Constants.ORDER_SBA && pos + 2 < data.Length)
            {
                currentRow = data[pos + 1];
                currentCol = data[pos + 2];
                pos += 3;
            }
            else if (b == Tn5250Constants.ORDER_SF && pos + 2 < data.Length)
            {
                byte ffw0 = data[pos + 1];
                byte ffw1 = data[pos + 2];
                pos += 3;

                // Record the field start for length computation
                fieldStarts.Add((currentRow, currentCol, ffw0, ffw1));

                // SF attribute byte occupies one column position
                AdvancePosition(ref currentRow, ref currentCol);
            }
            else if (b == Tn5250Constants.ORDER_IC)
            {
                buffer.CursorRow = currentRow;
                buffer.CursorCol = currentCol;
                pos++;
            }
            else if (b == Tn5250Constants.ORDER_RA && pos + 3 < data.Length)
            {
                int toRow = data[pos + 1];
                int toCol = data[pos + 2];
                byte fillByte = data[pos + 3];
                char fillChar = (char)EbcdicConverter.ToAscii(fillByte);
                pos += 4;

                buffer.FillRange(currentRow, currentCol, toRow, toCol, fillChar);
                currentRow = toRow;
                currentCol = toCol;
            }
            else if (b == Tn5250Constants.ORDER_SOH)
            {
                // Start of Header - skip the header length byte and the header data
                if (pos + 1 < data.Length)
                {
                    int headerLen = data[pos + 1];
                    pos += 2 + headerLen;
                }
                else
                {
                    pos++;
                }
            }
            else if (b == Tn5250Constants.ORDER_WEA || b == Tn5250Constants.ORDER_MC)
            {
                // Skip fixed-width orders that carry two additional bytes.
                if (pos + 2 >= data.Length)
                    throw new InvalidDataException($"Truncated order 0x{b:X2} at position {pos}.");
                pos += 3;
            }
            else if (b == Tn5250Constants.ORDER_WDSF ||
                     b == Tn5250Constants.ORDER_TD ||
                     b == Tn5250Constants.ORDER_EA)
            {
                // These orders are variable-width and require dedicated decoders.
                throw new InvalidDataException(
                    $"Unsupported variable-width order 0x{b:X2} at position {pos}.");
            }
            else
            {
                // Regular data byte - EBCDIC text
                char asciiChar = (char)EbcdicConverter.ToAscii(b);
                buffer.SetChar(currentRow, currentCol, asciiChar);
                AdvancePosition(ref currentRow, ref currentCol);
                pos++;
            }
        }

        // Compute field lengths from consecutive SF positions
        ComputeFieldLengths(fieldStarts, buffer);

        _log($"Screen parsed: {buffer.Fields.Count} fields, cursor at ({buffer.CursorRow},{buffer.CursorCol})");
    }

    private static void AdvancePosition(ref int row, ref int col)
    {
        col++;
        if (col > Tn5250Constants.SCREEN_COLS)
        {
            col = 1;
            row++;
        }
    }

    /// <summary>
    /// Computes field lengths by measuring the distance between consecutive SF orders.
    /// A field ends at the next SF order or end of row.
    /// </summary>
    private void ComputeFieldLengths(List<(int row, int col, byte ffw0, byte ffw1)> starts, ScreenBuffer buffer)
    {
        for (int i = 0; i < starts.Count; i++)
        {
            var (row, col, ffw0, ffw1) = starts[i];

            // Data starts at col+1 (after the SF attribute byte)
            int dataCol = col + 1;

            // Find the end of this field: either the next SF on the same row or end of a reasonable range
            int length;
            if (i + 1 < starts.Count && starts[i + 1].row == row)
            {
                // Next field on same row: length is distance between them minus 1 (for next SF attribute byte)
                length = starts[i + 1].col - dataCol;
            }
            else
            {
                // Last field on row or next field is on different row
                // Estimate length to end of row
                length = Tn5250Constants.SCREEN_COLS - dataCol + 1;

                // Look for next SF on subsequent row
                if (i + 1 < starts.Count)
                {
                    var next = starts[i + 1];
                    int linearDist = ((next.row - 1) * Tn5250Constants.SCREEN_COLS + next.col) -
                                     ((row - 1) * Tn5250Constants.SCREEN_COLS + dataCol);
                    if (linearDist > 0 && linearDist < length)
                        length = linearDist;
                }
            }

            if (length <= 0) length = 1;

            buffer.AddField(new ScreenField
            {
                Row = row,
                Col = col,
                Length = length,
                Ffw0 = ffw0,
                Ffw1 = ffw1
            });
        }
    }
}
