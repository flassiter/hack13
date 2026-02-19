using Hack13.Contracts.Utilities;

namespace Hack13.Contracts.Tests;

public class DataDictionaryExtensionsTests
{
    [Fact]
    public void GetRequired_MissingKey_ThrowsKeyNotFoundException()
    {
        var dict = new Dictionary<string, string>();
        Assert.Throws<KeyNotFoundException>(() => dict.GetRequired("missing"));
    }

    [Fact]
    public void GetOptional_MissingKey_ReturnsDefault()
    {
        var dict = new Dictionary<string, string>();
        Assert.Equal("fallback", dict.GetOptional("missing", "fallback"));
    }

    [Fact]
    public void GetDecimal_ValidValue_ReturnsDecimal()
    {
        var dict = new Dictionary<string, string> { ["Amount"] = "$1,000.50" };
        Assert.Equal(1000.50m, dict.GetDecimal("Amount"));
    }

    [Fact]
    public void GetBool_TrueVariants_ReturnTrue()
    {
        foreach (var val in new[] { "true", "True", "TRUE", "1", "yes", "Yes" })
        {
            var dict = new Dictionary<string, string> { ["Flag"] = val };
            Assert.True(dict.GetBool("Flag"), $"Expected true for '{val}'");
        }
    }

    [Fact]
    public void Set_Decimal_StoresAsString()
    {
        var dict = new Dictionary<string, string>();
        dict.Set("Total", 3.14m);
        Assert.Equal("3.14", dict["Total"]);
    }
}
