using System.Net.Sockets;
using Hack13.Contracts.Protocol;

namespace Hack13.TerminalClient.Protocol;

/// <summary>
/// Client-side telnet option negotiation for TN5250 connections.
/// Responds to server's DO/WILL with appropriate WILL/DO responses.
/// </summary>
public class ClientTelnetNegotiator
{
    private readonly NetworkStream _stream;
    private readonly Action<string> _log;
    private readonly string _terminalType;
    private readonly string? _deviceName;
    private readonly List<byte> _pendingData = new();

    public ClientTelnetNegotiator(
        NetworkStream stream,
        Action<string> log,
        string terminalType = "IBM-3179-2",
        string? deviceName = null)
    {
        _stream = stream;
        _log = log;
        _terminalType = terminalType;
        _deviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim();
    }

    public byte[] ConsumePendingData()
    {
        var data = _pendingData.ToArray();
        _pendingData.Clear();
        return data;
    }

    public async Task NegotiateAsync(CancellationToken ct)
    {
        bool eorWillSent = false;
        bool eorDoSent = false;
        bool binaryWillSent = false;
        bool binaryDoSent = false;
        bool terminalTypeRequested = false;
        bool terminalTypeSent = false;

        const int maxTokens = 128;
        int tokenCount = 0;

        while (tokenCount < maxTokens)
        {
            var token = await ReadTokenAsync(ct);
            tokenCount++;

            switch (token)
            {
                case TelnetToken.Data(var b):
                {
                    _pendingData.Add(b);
                    _log("Received non-negotiation data while negotiating");
                    if (IsNegotiationComplete(eorWillSent, eorDoSent, binaryWillSent, binaryDoSent, terminalTypeRequested, terminalTypeSent))
                        return;
                    break;
                }
                case TelnetToken.Option(var cmd, var option):
                {
                    if (cmd == Tn5250Constants.DO)
                    {
                        _log($"Server: DO 0x{option:X2}");
                        if (IsSupportedOption(option))
                        {
                            await SendAsync([Tn5250Constants.IAC, Tn5250Constants.WILL, option], ct);
                            _log($"Client: WILL 0x{option:X2}");
                            if (option == Tn5250Constants.OPT_END_OF_RECORD) eorWillSent = true;
                            if (option == Tn5250Constants.OPT_BINARY) binaryWillSent = true;
                            if (option == Tn5250Constants.OPT_TERMINAL_TYPE) terminalTypeRequested = true;
                        }
                        else
                        {
                            await SendAsync([Tn5250Constants.IAC, Tn5250Constants.WONT, option], ct);
                            _log($"Client: WONT 0x{option:X2}");
                        }
                    }
                    else if (cmd == Tn5250Constants.WILL)
                    {
                        _log($"Server: WILL 0x{option:X2}");
                        if (IsSupportedOption(option))
                        {
                            await SendAsync([Tn5250Constants.IAC, Tn5250Constants.DO, option], ct);
                            _log($"Client: DO 0x{option:X2}");
                            if (option == Tn5250Constants.OPT_END_OF_RECORD) eorDoSent = true;
                            if (option == Tn5250Constants.OPT_BINARY) binaryDoSent = true;
                        }
                        else
                        {
                            await SendAsync([Tn5250Constants.IAC, Tn5250Constants.DONT, option], ct);
                            _log($"Client: DONT 0x{option:X2}");
                        }
                    }

                    break;
                }
                case TelnetToken.Subnegotiation(var option, var payload):
                {
                    if (option == Tn5250Constants.OPT_TERMINAL_TYPE &&
                        payload.Length > 0 &&
                        payload[0] == Tn5250Constants.TERMINAL_TYPE_SEND)
                    {
                        _log("Server: SB TERMINAL-TYPE SEND");
                        await SendTerminalTypeAsync(ct);
                        _log($"Client: SB TERMINAL-TYPE IS {_terminalType}");
                        terminalTypeSent = true;
                    }
                    break;
                }
            }

            if (IsNegotiationComplete(eorWillSent, eorDoSent, binaryWillSent, binaryDoSent, terminalTypeRequested, terminalTypeSent))
            {
                _log("Telnet negotiation complete");
                return;
            }
        }

        throw new TimeoutException("Telnet negotiation did not complete in expected number of tokens");
    }

    private async Task SendAsync(byte[] data, CancellationToken ct)
    {
        await _stream.WriteAsync(data, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task SendTerminalTypeAsync(CancellationToken ct)
    {
        var terminalDeclaration = _deviceName == null
            ? _terminalType
            : $"{_terminalType}@{_deviceName}";
        var termTypeBytes = System.Text.Encoding.ASCII.GetBytes(terminalDeclaration);
        var response = new byte[6 + termTypeBytes.Length];
        response[0] = Tn5250Constants.IAC;
        response[1] = Tn5250Constants.SB;
        response[2] = Tn5250Constants.OPT_TERMINAL_TYPE;
        response[3] = Tn5250Constants.TERMINAL_TYPE_IS;
        Array.Copy(termTypeBytes, 0, response, 4, termTypeBytes.Length);
        response[4 + termTypeBytes.Length] = Tn5250Constants.IAC;
        response[5 + termTypeBytes.Length] = Tn5250Constants.SE;
        await SendAsync(response, ct);
    }

    private static bool IsSupportedOption(byte option)
    {
        return option == Tn5250Constants.OPT_TERMINAL_TYPE ||
               option == Tn5250Constants.OPT_END_OF_RECORD ||
               option == Tn5250Constants.OPT_BINARY;
    }

    private static bool IsNegotiationComplete(
        bool eorWillSent,
        bool eorDoSent,
        bool binaryWillSent,
        bool binaryDoSent,
        bool terminalTypeRequested,
        bool terminalTypeSent)
    {
        if (!eorWillSent || !eorDoSent || !binaryWillSent || !binaryDoSent)
            return false;
        return !terminalTypeRequested || terminalTypeSent;
    }

    private async Task<TelnetToken> ReadTokenAsync(CancellationToken ct)
    {
        var first = await ReadByteAsync(ct);
        if (first != Tn5250Constants.IAC)
            return new TelnetToken.Data(first);

        var command = await ReadByteAsync(ct);
        if (command == Tn5250Constants.IAC)
            return new TelnetToken.Data(Tn5250Constants.IAC);

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
                var b = await ReadByteAsync(ct);
                if (lastWasIac)
                {
                    if (b == Tn5250Constants.SE)
                        break;
                    if (b == Tn5250Constants.IAC)
                    {
                        payload.Add(Tn5250Constants.IAC);
                        lastWasIac = false;
                        continue;
                    }

                    payload.Add(Tn5250Constants.IAC);
                    payload.Add(b);
                    lastWasIac = false;
                    continue;
                }

                if (b == Tn5250Constants.IAC)
                {
                    lastWasIac = true;
                    continue;
                }

                payload.Add(b);
            }

            return new TelnetToken.Subnegotiation(option, [.. payload]);
        }

        return new TelnetToken.Command(command);
    }

    private async Task<byte> ReadByteAsync(CancellationToken ct)
    {
        var oneByte = new byte[1];
        var bytesRead = await _stream.ReadAsync(oneByte.AsMemory(0, 1), ct);
        if (bytesRead == 0) throw new IOException("Server disconnected during negotiation");
        return oneByte[0];
    }

    private abstract record TelnetToken
    {
        public sealed record Data(byte Value) : TelnetToken;
        public sealed record Option(byte CommandCode, byte OptionCode) : TelnetToken;
        public sealed record Subnegotiation(byte OptionCode, byte[] Payload) : TelnetToken;
        public sealed record Command(byte CommandCode) : TelnetToken;
    }
}
