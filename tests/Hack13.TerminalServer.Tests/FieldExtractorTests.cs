using Hack13.Contracts.Protocol;
using Hack13.Contracts.ScreenCatalog;
using Hack13.TerminalServer.Engine;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Tests;

public class FieldExtractorTests
{
    private readonly FieldExtractor _extractor = new();

    private static ScreenDefinition CreateLoginScreen()
    {
        return new ScreenDefinition
        {
            ScreenId = "sign_on",
            Identifier = new ScreenIdentifier { Row = 1, Col = 1, ExpectedText = "Sign On" },
            Fields = new List<FieldDefinition>
            {
                new() { Name = "user_id", Type = "input", Row = 10, Col = 35, Length = 10 },
                new() { Name = "password", Type = "input", Row = 12, Col = 35, Length = 10, Attributes = "hidden" }
            }
        };
    }

    [Fact]
    public void Extract_MatchesFieldByPosition()
    {
        var screen = CreateLoginScreen();
        var input = new InputRecord
        {
            AidKey = Tn5250Constants.AID_ENTER,
            CursorRow = 10,
            CursorCol = 36,
            Fields = new List<ModifiedField>
            {
                new() { Row = 10, Col = 36, Value = "TESTUSER" }
            }
        };

        var result = _extractor.Extract(input, screen);

        Assert.Single(result);
        Assert.Equal("TESTUSER", result["user_id"]);
    }

    [Fact]
    public void Extract_MultipleFields()
    {
        var screen = CreateLoginScreen();
        var input = new InputRecord
        {
            AidKey = Tn5250Constants.AID_ENTER,
            Fields = new List<ModifiedField>
            {
                new() { Row = 10, Col = 36, Value = "ADMIN" },
                new() { Row = 12, Col = 36, Value = "SECRET" }
            }
        };

        var result = _extractor.Extract(input, screen);

        Assert.Equal(2, result.Count);
        Assert.Equal("ADMIN", result["user_id"]);
        Assert.Equal("SECRET", result["password"]);
    }

    [Fact]
    public void Extract_NoMatchingField_ReturnsEmpty()
    {
        var screen = CreateLoginScreen();
        var input = new InputRecord
        {
            AidKey = Tn5250Constants.AID_ENTER,
            Fields = new List<ModifiedField>
            {
                new() { Row = 99, Col = 99, Value = "ORPHAN" }
            }
        };

        var result = _extractor.Extract(input, screen);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_IgnoresDisplayFields()
    {
        var screen = new ScreenDefinition
        {
            ScreenId = "test",
            Fields = new List<FieldDefinition>
            {
                new() { Name = "display_field", Type = "display", Row = 5, Col = 10, Length = 20 },
                new() { Name = "input_field", Type = "input", Row = 5, Col = 40, Length = 10 }
            }
        };

        var input = new InputRecord
        {
            Fields = new List<ModifiedField>
            {
                new() { Row = 5, Col = 11, Value = "VALUE" }
            }
        };

        var result = _extractor.Extract(input, screen);

        // Should not match the display field
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_EmptyInput_ReturnsEmpty()
    {
        var screen = CreateLoginScreen();
        var input = new InputRecord { AidKey = Tn5250Constants.AID_F3 };

        var result = _extractor.Extract(input, screen);

        Assert.Empty(result);
    }
}
