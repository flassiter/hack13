using Hack13.Contracts.Protocol;
using Hack13.Contracts.ScreenCatalog;
using Hack13.TerminalClient.Protocol;
using ScreenIdent = Hack13.TerminalClient.Screen.ScreenIdentifier;

namespace Hack13.TerminalClient.Tests;

public class ScreenIdentifierTests
{
    private static List<ScreenDefinition> CreateTestScreens() =>
    [
        new ScreenDefinition
        {
            ScreenId = "sign_on",
            Identifier = new Contracts.ScreenCatalog.ScreenIdentifier { Row = 1, Col = 28, ExpectedText = "Sign On" }
        },
        new ScreenDefinition
        {
            ScreenId = "loan_inquiry",
            Identifier = new Contracts.ScreenCatalog.ScreenIdentifier { Row = 1, Col = 25, ExpectedText = "Loan Inquiry" }
        },
        new ScreenDefinition
        {
            ScreenId = "loan_details",
            Identifier = new Contracts.ScreenCatalog.ScreenIdentifier { Row = 1, Col = 25, ExpectedText = "Loan Details" }
        },
        new ScreenDefinition
        {
            ScreenId = "escrow_analysis",
            Identifier = new Contracts.ScreenCatalog.ScreenIdentifier { Row = 1, Col = 23, ExpectedText = "Escrow Analysis" }
        }
    ];

    [Fact]
    public void Identify_returns_matching_screen()
    {
        var identifier = new ScreenIdent(CreateTestScreens());
        var buf = new ScreenBuffer();

        // Write "Sign On" at row 1, col 28
        var text = "Sign On";
        for (int i = 0; i < text.Length; i++)
            buf.SetChar(1, 28 + i, text[i]);

        var screen = identifier.Identify(buf);
        Assert.NotNull(screen);
        Assert.Equal("sign_on", screen.ScreenId);
    }

    [Fact]
    public void Identify_returns_null_for_unrecognized_screen()
    {
        var identifier = new ScreenIdent(CreateTestScreens());
        var buf = new ScreenBuffer(); // all spaces

        var screen = identifier.Identify(buf);
        Assert.Null(screen);
    }

    [Fact]
    public void IsScreen_returns_true_for_matching_screen()
    {
        var identifier = new ScreenIdent(CreateTestScreens());
        var buf = new ScreenBuffer();

        var text = "Loan Details";
        for (int i = 0; i < text.Length; i++)
            buf.SetChar(1, 25 + i, text[i]);

        Assert.True(identifier.IsScreen(buf, "loan_details"));
        Assert.False(identifier.IsScreen(buf, "sign_on"));
    }

    [Fact]
    public void IsScreen_returns_false_for_unknown_screen_id()
    {
        var identifier = new ScreenIdent(CreateTestScreens());
        var buf = new ScreenBuffer();

        Assert.False(identifier.IsScreen(buf, "nonexistent"));
    }

    [Theory]
    [InlineData("Sign On", 28, "sign_on")]
    [InlineData("Loan Inquiry", 25, "loan_inquiry")]
    [InlineData("Loan Details", 25, "loan_details")]
    [InlineData("Escrow Analysis", 23, "escrow_analysis")]
    public void Identify_matches_all_screen_types(string title, int col, string expectedId)
    {
        var identifier = new ScreenIdent(CreateTestScreens());
        var buf = new ScreenBuffer();

        for (int i = 0; i < title.Length; i++)
            buf.SetChar(1, col + i, title[i]);

        var screen = identifier.Identify(buf);
        Assert.NotNull(screen);
        Assert.Equal(expectedId, screen.ScreenId);
    }

    [Fact]
    public void Identify_is_case_insensitive_and_ignores_trailing_spaces()
    {
        var identifier = new ScreenIdent(CreateTestScreens());
        var buf = new ScreenBuffer();

        var text = "loan details";
        for (int i = 0; i < text.Length; i++)
            buf.SetChar(1, 25 + i, text[i]);

        var screen = identifier.Identify(buf);
        Assert.NotNull(screen);
        Assert.Equal("loan_details", screen.ScreenId);
    }
}
