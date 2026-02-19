using Hack13.Contracts.Protocol;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Tests;

public class EbcdicConverterTests
{
    [Theory]
    [InlineData('A', 0xC1)]
    [InlineData('Z', 0xE9)]
    [InlineData('0', 0xF0)]
    [InlineData('9', 0xF9)]
    [InlineData(' ', 0x40)]
    [InlineData('.', 0x4B)]
    [InlineData('$', 0x5B)]
    [InlineData('-', 0x60)]
    public void FromAscii_ConvertsCorrectly(char ascii, byte expectedEbcdic)
    {
        var result = EbcdicConverter.FromAscii((byte)ascii);
        Assert.Equal(expectedEbcdic, result);
    }

    [Theory]
    [InlineData(0xC1, 'A')]
    [InlineData(0xE9, 'Z')]
    [InlineData(0xF0, '0')]
    [InlineData(0xF9, '9')]
    [InlineData(0x40, ' ')]
    public void ToAscii_ConvertsCorrectly(byte ebcdic, char expectedAscii)
    {
        var result = EbcdicConverter.ToAscii(ebcdic);
        Assert.Equal((byte)expectedAscii, result);
    }

    [Fact]
    public void FromAscii_String_RoundTrips()
    {
        var original = "HELLO WORLD 123";
        var ebcdic = EbcdicConverter.FromAscii(original);
        var result = EbcdicConverter.ToAscii(ebcdic, 0, ebcdic.Length);
        Assert.Equal(original, result);
    }

    [Fact]
    public void RoundTrip_AllUppercase()
    {
        var original = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var ebcdic = EbcdicConverter.FromAscii(original);
        var result = EbcdicConverter.ToAscii(ebcdic, 0, ebcdic.Length);
        Assert.Equal(original, result);
    }

    [Fact]
    public void RoundTrip_AllDigits()
    {
        var original = "0123456789";
        var ebcdic = EbcdicConverter.FromAscii(original);
        var result = EbcdicConverter.ToAscii(ebcdic, 0, ebcdic.Length);
        Assert.Equal(original, result);
    }

    [Fact]
    public void RoundTrip_CommonPunctuation()
    {
        var original = ".$,()-/:;=*+@#!?";
        var ebcdic = EbcdicConverter.FromAscii(original);
        var result = EbcdicConverter.ToAscii(ebcdic, 0, ebcdic.Length);
        Assert.Equal(original, result);
    }

    [Fact]
    public void ToAscii_Span_Works()
    {
        var ebcdic = EbcdicConverter.FromAscii("TEST");
        ReadOnlySpan<byte> span = ebcdic;
        var result = EbcdicConverter.ToAscii(span);
        Assert.Equal("TEST", result);
    }
}
