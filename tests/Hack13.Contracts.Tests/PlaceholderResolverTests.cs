using Hack13.Contracts.Utilities;

namespace Hack13.Contracts.Tests;

public class PlaceholderResolverTests
{
    [Fact]
    public void Resolve_ReplacesKnownPlaceholders()
    {
        var data = new Dictionary<string, string> { ["LoanNumber"] = "12345", ["BorrowerName"] = "Jane Doe" };
        var result = PlaceholderResolver.Resolve("Loan {{LoanNumber}} for {{BorrowerName}}", data);
        Assert.Equal("Loan 12345 for Jane Doe", result);
    }

    [Fact]
    public void Resolve_LeavesUnknownPlaceholdersIntact()
    {
        var data = new Dictionary<string, string>();
        var result = PlaceholderResolver.Resolve("Hello {{Name}}", data);
        Assert.Equal("Hello {{Name}}", result);
    }

    [Fact]
    public void Resolve_EmptyTemplate_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PlaceholderResolver.Resolve(string.Empty, new Dictionary<string, string>()));
    }

    [Fact]
    public void GetPlaceholderKeys_ReturnsDistinctKeys()
    {
        var keys = PlaceholderResolver.GetPlaceholderKeys("{{A}} and {{B}} and {{A}}").ToList();
        Assert.Equal(2, keys.Count);
        Assert.Contains("A", keys);
        Assert.Contains("B", keys);
    }
}
