using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Hack13.Contracts.Protocol;

namespace Hack13.TerminalServer.Protocol;

/// <summary>
/// Parsed result from a client input data stream.
/// </summary>
public class InputRecord
{
    public byte AidKey { get; set; }
    public int CursorRow { get; set; }
    public int CursorCol { get; set; }
    public List<ModifiedField> Fields { get; set; } = new();

    public string AidKeyName => Tn5250Constants.AidKeyName(AidKey);
}

/// <summary>
/// A single modified field from client input.
/// </summary>
public class ModifiedField
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Reads and parses 5250 input data streams from the terminal.
/// Handles EOR framing and IAC escaping.
/// </summary>
public class DataStreamReader
{
    private readonly NetworkStream _stream;
    private readonly ILogger _logger;
    private readonly TimeSpan _readTimeout;

    public DataStreamReader(NetworkStream stream, ILogger logger, TimeSpan? readTimeout = null)
    {
        _stream = stream;
        _logger = logger;
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Reads one complete EOR-delimited record from the stream,
    /// handling IAC escaping, and parses the 5250 input fields.
    /// </summary>
    public async Task<InputRecord> ReadInputAsync(CancellationToken ct)
    {
        var data = await ReadEorFrameAsync(ct);
        return ParseInputRecord(data);
    }

    /// <summary>
    /// Reads bytes from the stream until IAC EOR is encountered.
    /// Handles IAC IAC escaping (0xFF 0xFF → single 0xFF).
    /// </summary>
    private async Task<byte[]> ReadEorFrameAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();

        while (true)
        {
            byte b = await ReadByteAsync(ct);

            if (b == Tn5250Constants.IAC)
            {
                var command = await ReadByteAsync(ct);

                if (command == Tn5250Constants.EOR)
                    return buffer.ToArray();

                if (command == Tn5250Constants.IAC)
                {
                    // Escaped 0xFF in payload.
                    buffer.WriteByte(Tn5250Constants.IAC);
                    continue;
                }

                // Telnet option command: consume option byte.
                if (command is Tn5250Constants.DO or Tn5250Constants.DONT
                    or Tn5250Constants.WILL or Tn5250Constants.WONT)
                {
                    var option = await ReadByteAsync(ct);
                    _logger.LogDebug("Ignoring in-band telnet command IAC 0x{Command:X2} 0x{Option:X2}", command, option);
                    continue;
                }

                // Telnet subnegotiation: consume until IAC SE.
                if (command == Tn5250Constants.SB)
                {
                    await ConsumeSubnegotiationAsync(ct);
                    continue;
                }

                _logger.LogDebug("Ignoring in-band telnet command IAC 0x{Command:X2}", command);
                continue;
            }

            buffer.WriteByte(b);
        }
    }

    /// <summary>
    /// Parses a raw 5250 input record into structured data.
    /// Format: [GDS header (10 bytes)] [cursor_row] [cursor_col] [AID] [modified fields...]
    /// </summary>
    private InputRecord ParseInputRecord(byte[] data)
    {
        var result = new InputRecord();

        if (data.Length < 13)
        {
            _logger.LogWarning("Input record too short ({Length} bytes): {Data}",
                data.Length, BitConverter.ToString(data));
            return result;
        }

        // Skip GDS header (first 10 bytes)
        int pos = 10;

        result.CursorRow = data[pos++];
        result.CursorCol = data[pos++];
        result.AidKey = data[pos++];

        _logger.LogDebug("Input: AID={AidKey}, Cursor=({Row},{Col})",
            result.AidKeyName, result.CursorRow, result.CursorCol);

        // Parse modified fields: SBA (0x11) + row + col + field data
        while (pos < data.Length)
        {
            if (data[pos] == Tn5250Constants.ORDER_SBA && pos + 2 < data.Length)
            {
                pos++; // skip SBA order byte
                int fieldRow = data[pos++];
                int fieldCol = data[pos++];

                // Read field data until next SBA or end of record
                int dataStart = pos;
                while (pos < data.Length && data[pos] != Tn5250Constants.ORDER_SBA)
                {
                    pos++;
                }

                var fieldData = EbcdicConverter.ToAscii(data, dataStart, pos - dataStart).TrimEnd();

                result.Fields.Add(new ModifiedField
                {
                    Row = fieldRow,
                    Col = fieldCol,
                    Value = fieldData
                });

                _logger.LogDebug("  Field at ({Row},{Col}): \"{Value}\"",
                    fieldRow, fieldCol, fieldData);
            }
            else
            {
                pos++;
            }
        }

        return result;
    }

    private async Task<byte> ReadByteAsync(CancellationToken ct)
    {
        var oneByte = new byte[1];
        var readTask = _stream.ReadAsync(oneByte, ct).AsTask();
        var completed = await Task.WhenAny(readTask, Task.Delay(_readTimeout, ct));
        if (completed != readTask)
            throw new TimeoutException($"Timed out reading TN5250 input after {_readTimeout.TotalSeconds:0} seconds.");

        var bytesRead = await readTask;
        if (bytesRead == 0) throw new IOException("Client disconnected");
        return oneByte[0];
    }

    private async Task ConsumeSubnegotiationAsync(CancellationToken ct)
    {
        bool lastWasIac = false;
        while (true)
        {
            var b = await ReadByteAsync(ct);
            if (lastWasIac && b == Tn5250Constants.SE)
                return;
            lastWasIac = b == Tn5250Constants.IAC;
        }
    }
}
