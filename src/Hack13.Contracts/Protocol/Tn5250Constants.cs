namespace Hack13.Contracts.Protocol;

/// <summary>
/// Byte-level constants for the TN5250 protocol.
/// References: RFC 4777, IBM 5250 Functions Reference.
/// </summary>
public static class Tn5250Constants
{
    // ── Telnet commands ──
    public const byte IAC = 0xFF;
    public const byte DO = 0xFD;
    public const byte DONT = 0xFE;
    public const byte WILL = 0xFB;
    public const byte WONT = 0xFC;
    public const byte SB = 0xFA;   // Subnegotiation Begin
    public const byte SE = 0xF0;   // Subnegotiation End
    public const byte EOR = 0xEF;  // End of Record

    // ── Telnet options ──
    public const byte OPT_BINARY = 0x00;
    public const byte OPT_TERMINAL_TYPE = 0x18;
    public const byte OPT_END_OF_RECORD = 0x19;
    public const byte OPT_NEW_ENVIRON = 0x27;

    // ── Terminal type subnegotiation ──
    public const byte TERMINAL_TYPE_IS = 0x00;
    public const byte TERMINAL_TYPE_SEND = 0x01;

    // ── 5250 escape and commands ──
    public const byte ESC = 0x04;
    public const byte CMD_CLEAR_UNIT = 0x40;
    public const byte CMD_WRITE_TO_DISPLAY = 0x11;
    public const byte CMD_WRITE_STRUCTURED_FIELD = 0xF3;

    // ── 5250 orders (within WTD body) ──
    public const byte ORDER_SOH = 0x01;   // Start of Header
    public const byte ORDER_RA = 0x02;    // Repeat to Address
    public const byte ORDER_EA = 0x03;    // Erase to Address
    public const byte ORDER_TD = 0x10;    // Transparent Data
    public const byte ORDER_SBA = 0x11;   // Set Buffer Address
    public const byte ORDER_WEA = 0x12;   // Write Extended Attribute
    public const byte ORDER_IC = 0x13;    // Insert Cursor
    public const byte ORDER_MC = 0x14;    // Move Cursor
    public const byte ORDER_SF = 0x1D;    // Start of Field
    public const byte ORDER_WDSF = 0x15;  // Write to Display Structured Field

    // ── GDS (General Data Stream) record constants ──
    public const ushort GDS_RECORD_TYPE = 0x12A0;
    public const byte GDS_OPCODE_NO_OP = 0x00;
    public const byte GDS_OPCODE_INVITE = 0x01;
    public const byte GDS_OPCODE_OUTPUT_ONLY = 0x02;
    public const byte GDS_OPCODE_PUT_GET = 0x03;
    public const byte GDS_OPCODE_SAVE_SCREEN = 0x04;
    public const byte GDS_OPCODE_RESTORE_SCREEN = 0x05;
    public const byte GDS_OPCODE_READ_SCREEN = 0x08;
    public const byte GDS_OPCODE_CANCEL_INVITE = 0x0A;
    public const byte GDS_OPCODE_MSG_LIGHT_ON = 0x11;
    public const byte GDS_OPCODE_MSG_LIGHT_OFF = 0x12;

    // ── FFW (Field Format Word) byte 0 flags ──
    public const byte FFW_BYPASS = 0x20;           // Protected / display-only
    public const byte FFW_DUP_ENABLE = 0x10;
    public const byte FFW_MDT = 0x08;              // Modified Data Tag
    public const byte FFW_SHIFT_ALPHA = 0x00;      // Alphanumeric (default)
    public const byte FFW_SHIFT_ALPHA_ONLY = 0x01;
    public const byte FFW_SHIFT_NUMERIC = 0x02;
    public const byte FFW_SHIFT_NUMERIC_ONLY = 0x03;
    public const byte FFW_SHIFT_DIGITS_ONLY = 0x05;
    public const byte FFW_SHIFT_SIGNED_NUMERIC = 0x06;
    public const byte FFW_SHIFT_NONDISPLAY = 0x07; // Non-display (hidden)

    // ── FFW byte 1 flags ──
    public const byte FFW_AUTO_ENTER = 0x80;
    public const byte FFW_FIELD_EXIT_REQUIRED = 0x40;
    public const byte FFW_MONOCASE = 0x20;
    public const byte FFW_MANDATORY_ENTRY = 0x08;

    // ── AID key bytes (terminal → host) ──
    public const byte AID_ENTER = 0xF1;
    public const byte AID_F1 = 0x31;
    public const byte AID_F2 = 0x32;
    public const byte AID_F3 = 0x33;
    public const byte AID_F4 = 0x34;
    public const byte AID_F5 = 0x35;
    public const byte AID_F6 = 0x36;
    public const byte AID_F7 = 0x37;
    public const byte AID_F8 = 0x38;
    public const byte AID_F9 = 0x39;
    public const byte AID_F10 = 0x3A;
    public const byte AID_F11 = 0x3B;
    public const byte AID_F12 = 0x3C;
    public const byte AID_ROLL_UP = 0xF5;    // Page Down
    public const byte AID_ROLL_DOWN = 0xF4;  // Page Up
    public const byte AID_HELP = 0xF3;
    public const byte AID_PRINT = 0xF6;
    public const byte AID_CLEAR = 0xBD;

    // ── Screen dimensions ──
    public const int SCREEN_ROWS = 24;
    public const int SCREEN_COLS = 80;

    // ── WTD control characters ──
    public const byte CC1_LOCK_KEYBOARD = 0x20;
    public const byte CC1_RESET_MDT = 0x00;
    public const byte CC2_NO_FLAGS = 0x00;

    public static byte AidKeyFromName(string name) => name switch
    {
        "Enter" => AID_ENTER,
        "F1" => AID_F1,
        "F2" => AID_F2,
        "F3" => AID_F3,
        "F4" => AID_F4,
        "F5" => AID_F5,
        "F6" => AID_F6,
        "F7" => AID_F7,
        "F8" => AID_F8,
        "F9" => AID_F9,
        "F10" => AID_F10,
        "F11" => AID_F11,
        "F12" => AID_F12,
        "PageDown" => AID_ROLL_UP,
        "PageUp" => AID_ROLL_DOWN,
        "Help" => AID_HELP,
        "Print" => AID_PRINT,
        "Clear" => AID_CLEAR,
        _ => throw new ArgumentException($"Unknown AID key: {name}")
    };

    public static string AidKeyName(byte aid) => aid switch
    {
        AID_ENTER => "Enter",
        AID_F1 => "F1",
        AID_F2 => "F2",
        AID_F3 => "F3",
        AID_F4 => "F4",
        AID_F5 => "F5",
        AID_F6 => "F6",
        AID_F7 => "F7",
        AID_F8 => "F8",
        AID_F9 => "F9",
        AID_F10 => "F10",
        AID_F11 => "F11",
        AID_F12 => "F12",
        AID_ROLL_UP => "PageDown",
        AID_ROLL_DOWN => "PageUp",
        AID_HELP => "Help",
        AID_PRINT => "Print",
        AID_CLEAR => "Clear",
        _ => $"Unknown(0x{aid:X2})"
    };
}
