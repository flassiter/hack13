using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Hack13.Contracts.Protocol;

namespace Hack13.TerminalServer.Protocol;

/// <summary>
/// Handles the telnet option negotiation phase of a TN5250 connection.
/// Negotiates TERMINAL-TYPE, END-OF-RECORD, and BINARY options.
/// </summary>
public class TelnetNegotiator
{
    private readonly NetworkStream _stream;
    private readonly ILogger _logger;
    private readonly TimeSpan _readTimeout;

    public string TerminalType { get; private set; } = "IBM-3179-2";
    public string? DeviceName { get; private set; }

    public TelnetNegotiator(NetworkStream stream, ILogger logger, TimeSpan? readTimeout = null)
    {
        _stream = stream;
        _logger = logger;
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task NegotiateAsync(CancellationToken ct)
    {
        // Step 1: Negotiate TERMINAL-TYPE
        await SendAsync([Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_TERMINAL_TYPE], ct);
        await ExpectWillAsync(Tn5250Constants.OPT_TERMINAL_TYPE, required: true, ct);

        // Request terminal type value
        await SendAsync(
        [
            Tn5250Constants.IAC, Tn5250Constants.SB, Tn5250Constants.OPT_TERMINAL_TYPE,
            Tn5250Constants.TERMINAL_TYPE_SEND,
            Tn5250Constants.IAC, Tn5250Constants.SE
        ], ct);
        await ReadTerminalTypeAsync(ct);
        _logger.LogInformation("Terminal type: {TerminalType}", TerminalType);

        // Step 2: Negotiate END-OF-RECORD (both directions)
        await SendAsync([Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_END_OF_RECORD], ct);
        await ExpectWillAsync(Tn5250Constants.OPT_END_OF_RECORD, required: true, ct);

        await SendAsync([Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_END_OF_RECORD], ct);
        await ExpectDoAsync(Tn5250Constants.OPT_END_OF_RECORD, required: true, ct);

        // Step 3: Negotiate BINARY (both directions)
        await SendAsync([Tn5250Constants.IAC, Tn5250Constants.DO, Tn5250Constants.OPT_BINARY], ct);
        await ExpectWillAsync(Tn5250Constants.OPT_BINARY, required: true, ct);

        await SendAsync([Tn5250Constants.IAC, Tn5250Constants.WILL, Tn5250Constants.OPT_BINARY], ct);
        await ExpectDoAsync(Tn5250Constants.OPT_BINARY, required: true, ct);

        _logger.LogInformation("Telnet negotiation complete");
    }

    private async Task SendAsync(byte[] data, CancellationToken ct)
    {
        await _stream.WriteAsync(data, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task<byte> ReadByteAsync(CancellationToken ct)
    {
        var oneByte = new byte[1];
        var readTask = _stream.ReadAsync(oneByte, ct).AsTask();
        var completed = await Task.WhenAny(readTask, Task.Delay(_readTimeout, ct));
        if (completed != readTask)
            throw new TimeoutException($"Timed out waiting for telnet negotiation data after {_readTimeout.TotalSeconds:0} seconds.");

        var bytesRead = await readTask;
        if (bytesRead == 0) throw new IOException("Client disconnected during negotiation");
        return oneByte[0];
    }

    private async Task ExpectWillAsync(byte option, bool required, CancellationToken ct)
    {
        while (true)
        {
            var token = await ReadTelnetTokenAsync(ct);
            if (token is TelnetToken.Option(var cmd, var opt) && opt == option)
            {
                if (cmd == Tn5250Constants.WILL) return;
                if (cmd == Tn5250Constants.WONT)
                {
                    if (required)
                        throw new InvalidOperationException($"Client refused required telnet option 0x{option:X2}.");

                    _logger.LogWarning("Client refused option 0x{Option:X2}", option);
                    return;
                }
            }
        }
    }

    private async Task ExpectDoAsync(byte option, bool required, CancellationToken ct)
    {
        while (true)
        {
            var token = await ReadTelnetTokenAsync(ct);
            if (token is TelnetToken.Option(var cmd, var opt) && opt == option)
            {
                if (cmd == Tn5250Constants.DO) return;
                if (cmd == Tn5250Constants.DONT)
                {
                    if (required)
                        throw new InvalidOperationException($"Client refused required telnet option 0x{option:X2}.");

                    _logger.LogWarning("Client refused option 0x{Option:X2}", option);
                    return;
                }
            }
        }
    }

    private async Task ReadTerminalTypeAsync(CancellationToken ct)
    {
        while (true)
        {
            var token = await ReadTelnetTokenAsync(ct);
            if (token is not TelnetToken.Subnegotiation(var subOption, var payload))
                continue;

            if (subOption != Tn5250Constants.OPT_TERMINAL_TYPE || payload.Length == 0)
                continue;

            if (payload[0] != Tn5250Constants.TERMINAL_TYPE_IS)
                continue;

            if (payload.Length > 1)
            {
                var declaredType = System.Text.Encoding.ASCII.GetString(payload[1..]);
                var atIndex = declaredType.IndexOf('@');
                if (atIndex > 0 && atIndex < declaredType.Length - 1)
                {
                    TerminalType = declaredType[..atIndex];
                    DeviceName = declaredType[(atIndex + 1)..];
                }
                else
                {
                    TerminalType = declaredType;
                    DeviceName = null;
                }
                return;
            }
        }
    }

    private async Task<TelnetToken> ReadTelnetTokenAsync(CancellationToken ct)
    {
        while (true)
        {
            var b = await ReadByteAsync(ct);
            if (b != Tn5250Constants.IAC)
                continue;

            var command = await ReadByteAsync(ct);
            if (command == Tn5250Constants.IAC)
                continue; // Escaped 0xFF in data stream.

            if (command is Tn5250Constants.DO or Tn5250Constants.DONT
                or Tn5250Constants.WILL or Tn5250Constants.WONT)
            {
                var option = await ReadByteAsync(ct);
                return new TelnetToken.Option(command, option);
            }

            if (command == Tn5250Constants.SB)
            {
                var option = await ReadByteAsync(ct);
                var payload = new List<byte>();
                bool lastWasIac = false;
                while (true)
                {
                    var ch = await ReadByteAsync(ct);
                    if (lastWasIac)
                    {
                        if (ch == Tn5250Constants.SE)
                            break;
                        if (ch == Tn5250Constants.IAC)
                        {
                            payload.Add(Tn5250Constants.IAC);
                            lastWasIac = false;
                            continue;
                        }

                        payload.Add(Tn5250Constants.IAC);
                        payload.Add(ch);
                        lastWasIac = false;
                        continue;
                    }

                    if (ch == Tn5250Constants.IAC)
                    {
                        lastWasIac = true;
                        continue;
                    }

                    payload.Add(ch);
                }

                return new TelnetToken.Subnegotiation(option, [.. payload]);
            }

            return new TelnetToken.Command(command);
        }
    }

    private abstract record TelnetToken
    {
        public sealed record Option(byte CommandCode, byte OptionCode) : TelnetToken;
        public sealed record Subnegotiation(byte OptionCode, byte[] Payload) : TelnetToken;
        public sealed record Command(byte CommandCode) : TelnetToken;
    }
}
