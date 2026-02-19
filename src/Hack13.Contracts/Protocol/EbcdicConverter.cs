namespace Hack13.Contracts.Protocol;

/// <summary>
/// Converts between ASCII and EBCDIC code page 037.
/// Only maps printable characters needed for green-screen interaction.
/// </summary>
public static class EbcdicConverter
{
    private static readonly byte[] AsciiToEbcdic = new byte[256];
    private static readonly byte[] EbcdicToAscii = new byte[256];

    static EbcdicConverter()
    {
        // Initialize both tables to space (EBCDIC 0x40 / ASCII 0x20)
        Array.Fill(AsciiToEbcdic, (byte)0x40);
        Array.Fill(EbcdicToAscii, (byte)0x20);

        // Build mapping from (ascii, ebcdic) pairs
        byte[][] map =
        [
            // Control / whitespace
            [0x00, 0x00], // NUL
            [0x20, 0x40], // Space

            // Digits 0-9
            [0x30, 0xF0], [0x31, 0xF1], [0x32, 0xF2], [0x33, 0xF3], [0x34, 0xF4],
            [0x35, 0xF5], [0x36, 0xF6], [0x37, 0xF7], [0x38, 0xF8], [0x39, 0xF9],

            // Uppercase A-I
            [0x41, 0xC1], [0x42, 0xC2], [0x43, 0xC3], [0x44, 0xC4], [0x45, 0xC5],
            [0x46, 0xC6], [0x47, 0xC7], [0x48, 0xC8], [0x49, 0xC9],
            // Uppercase J-R
            [0x4A, 0xD1], [0x4B, 0xD2], [0x4C, 0xD3], [0x4D, 0xD4], [0x4E, 0xD5],
            [0x4F, 0xD6], [0x50, 0xD7], [0x51, 0xD8], [0x52, 0xD9],
            // Uppercase S-Z
            [0x53, 0xE2], [0x54, 0xE3], [0x55, 0xE4], [0x56, 0xE5], [0x57, 0xE6],
            [0x58, 0xE7], [0x59, 0xE8], [0x5A, 0xE9],

            // Lowercase a-i
            [0x61, 0x81], [0x62, 0x82], [0x63, 0x83], [0x64, 0x84], [0x65, 0x85],
            [0x66, 0x86], [0x67, 0x87], [0x68, 0x88], [0x69, 0x89],
            // Lowercase j-r
            [0x6A, 0x91], [0x6B, 0x92], [0x6C, 0x93], [0x6D, 0x94], [0x6E, 0x95],
            [0x6F, 0x96], [0x70, 0x97], [0x71, 0x98], [0x72, 0x99],
            // Lowercase s-z
            [0x73, 0xA2], [0x74, 0xA3], [0x75, 0xA4], [0x76, 0xA5], [0x77, 0xA6],
            [0x78, 0xA7], [0x79, 0xA8], [0x7A, 0xA9],

            // Punctuation and symbols
            [0x21, 0x5A], // !
            [0x22, 0x7F], // "
            [0x23, 0x7B], // #
            [0x24, 0x5B], // $
            [0x25, 0x6C], // %
            [0x26, 0x50], // &
            [0x27, 0x7D], // '
            [0x28, 0x4D], // (
            [0x29, 0x5D], // )
            [0x2A, 0x5C], // *
            [0x2B, 0x4E], // +
            [0x2C, 0x6B], // ,
            [0x2D, 0x60], // -
            [0x2E, 0x4B], // .
            [0x2F, 0x61], // /
            [0x3A, 0x7A], // :
            [0x3B, 0x5E], // ;
            [0x3C, 0x4C], // <
            [0x3D, 0x7E], // =
            [0x3E, 0x6E], // >
            [0x3F, 0x6F], // ?
            [0x40, 0x7C], // @
            [0x5B, 0xBA], // [
            [0x5C, 0xE0], // backslash
            [0x5D, 0xBB], // ]
            [0x5E, 0xB0], // ^
            [0x5F, 0x6D], // _
            [0x60, 0x79], // `
            [0x7B, 0xC0], // {
            [0x7C, 0x4F], // |
            [0x7D, 0xD0], // }
            [0x7E, 0xA1], // ~
        ];

        foreach (var pair in map)
        {
            AsciiToEbcdic[pair[0]] = pair[1];
            EbcdicToAscii[pair[1]] = pair[0];
        }
    }

    public static byte FromAscii(byte ascii) => AsciiToEbcdic[ascii];

    public static byte ToAscii(byte ebcdic) => EbcdicToAscii[ebcdic];

    public static byte[] FromAscii(string text)
    {
        var result = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            result[i] = AsciiToEbcdic[(byte)text[i]];
        }
        return result;
    }

    public static string ToAscii(byte[] ebcdic, int offset, int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = (char)EbcdicToAscii[ebcdic[offset + i]];
        }
        return new string(chars);
    }

    public static string ToAscii(ReadOnlySpan<byte> ebcdic)
    {
        var chars = new char[ebcdic.Length];
        for (int i = 0; i < ebcdic.Length; i++)
        {
            chars[i] = (char)EbcdicToAscii[ebcdic[i]];
        }
        return new string(chars);
    }
}
