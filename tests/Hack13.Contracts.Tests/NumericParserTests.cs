using Hack13.Contracts.Utilities;

namespace Hack13.Contracts.Tests;

public class NumericParserTests
{
    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("(1,234.56)", -1234.56)]
    [InlineData("(500)", -500)]
    [InlineData("0", 0)]
    [InlineData("-42.5", -42.5)]
    public void TryParse_ValidInputs_ReturnExpectedValue(string input, double expected)
    {
        Assert.True(NumericParser.TryParse(input, out var result));
        Assert.Equal((decimal)expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void TryParse_InvalidInputs_ReturnsFalse(string? input)
    {
        Assert.False(NumericParser.TryParse(input, out _));
    }

    [Fact]
    public void Parse_InvalidInput_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => NumericParser.Parse("not-a-number"));
    }
}
