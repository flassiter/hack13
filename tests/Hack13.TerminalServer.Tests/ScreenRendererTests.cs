using Hack13.Contracts.Protocol;
using Hack13.Contracts.ScreenCatalog;
using Hack13.TerminalServer.Engine;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Tests;

public class ScreenRendererTests
{
    private readonly ScreenRenderer _renderer = new();

    private static ScreenDefinition CreateSimpleScreen()
    {
        return new ScreenDefinition
        {
            ScreenId = "test_screen",
            Identifier = new ScreenIdentifier { Row = 1, Col = 1, ExpectedText = "Test" },
            StaticText = new List<StaticTextElement>
            {
                new() { Row = 1, Col = 10, Text = "TEST SCREEN" }
            },
            Fields = new List<FieldDefinition>
            {
                new() { Name = "field1", Type = "input", Row = 5, Col = 20, Length = 10 },
                new() { Name = "display1", Type = "display", Row = 3, Col = 20, Length = 15 }
            }
        };
    }

    [Fact]
    public void RenderScreen_ProducesValidEorFrame()
    {
        var screen = CreateSimpleScreen();
        var data = _renderer.RenderScreen(screen, new Dictionary<string, string>());

        // Must end with IAC EOR
        Assert.Equal(Tn5250Constants.IAC, data[^2]);
        Assert.Equal(Tn5250Constants.EOR, data[^1]);
    }

    [Fact]
    public void RenderScreen_ContainsClearUnit()
    {
        var screen = CreateSimpleScreen();
        var data = _renderer.RenderScreen(screen, new Dictionary<string, string>());

        // Should contain ESC + Clear Unit somewhere after header
        bool found = false;
        for (int i = 10; i < data.Length - 1; i++)
        {
            if (data[i] == Tn5250Constants.ESC && data[i + 1] == Tn5250Constants.CMD_CLEAR_UNIT)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "Expected ESC + CLEAR_UNIT in output");
    }

    [Fact]
    public void RenderScreen_ContainsWriteToDisplay()
    {
        var screen = CreateSimpleScreen();
        var data = _renderer.RenderScreen(screen, new Dictionary<string, string>());

        bool found = false;
        for (int i = 10; i < data.Length - 1; i++)
        {
            if (data[i] == Tn5250Constants.ESC && data[i + 1] == Tn5250Constants.CMD_WRITE_TO_DISPLAY)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "Expected ESC + WTD in output");
    }

    [Fact]
    public void RenderScreen_ContainsSbaOrders()
    {
        var screen = CreateSimpleScreen();
        var data = _renderer.RenderScreen(screen, new Dictionary<string, string>());

        int sbaCount = 0;
        for (int i = 10; i < data.Length - 2; i++)
        {
            if (data[i] == Tn5250Constants.ORDER_SBA)
                sbaCount++;
        }

        // At least one SBA per static text + one per field + one for cursor
        Assert.True(sbaCount >= 3, $"Expected at least 3 SBA orders, got {sbaCount}");
    }

    [Fact]
    public void RenderScreen_ContainsSfOrders()
    {
        var screen = CreateSimpleScreen();
        var data = _renderer.RenderScreen(screen, new Dictionary<string, string>());

        int sfCount = 0;
        for (int i = 10; i < data.Length - 2; i++)
        {
            if (data[i] == Tn5250Constants.ORDER_SF)
                sfCount++;
        }

        // At least one SF per field (input gets SF + end-of-field SF, display gets SF)
        Assert.True(sfCount >= 2, $"Expected at least 2 SF orders, got {sfCount}");
    }

    [Fact]
    public void RenderScreen_ContainsInsertCursor()
    {
        var screen = CreateSimpleScreen();
        var data = _renderer.RenderScreen(screen, new Dictionary<string, string>());

        bool found = data.Skip(10).Any(b => b == Tn5250Constants.ORDER_IC);
        Assert.True(found, "Expected IC order for cursor placement");
    }

    [Fact]
    public void RenderScreen_SubstitutesDataValues()
    {
        var screen = CreateSimpleScreen();
        var data = _renderer.RenderScreen(screen, new Dictionary<string, string>
        {
            ["display1"] = "TESTVALUE"
        });

        // Convert the entire output to find EBCDIC "TESTVALUE"
        var testValueEbcdic = EbcdicConverter.FromAscii("TESTVALUE");

        bool found = false;
        for (int i = 0; i < data.Length - testValueEbcdic.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < testValueEbcdic.Length; j++)
            {
                if (data[i + j] != testValueEbcdic[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) { found = true; break; }
        }

        Assert.True(found, "Expected data value 'TESTVALUE' in EBCDIC in output");
    }

    [Fact]
    public void RenderScreen_WithErrorMessage_ContainsError()
    {
        var screen = CreateSimpleScreen();
        var data = _renderer.RenderScreen(screen, new Dictionary<string, string>(), "Test error");

        var errorEbcdic = EbcdicConverter.FromAscii("Test error");
        bool found = false;
        for (int i = 0; i < data.Length - errorEbcdic.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < errorEbcdic.Length; j++)
            {
                if (data[i + j] != errorEbcdic[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) { found = true; break; }
        }

        Assert.True(found, "Expected error message in output");
    }

    [Fact]
    public void RenderScreen_HiddenField_UsesNondisplayFfw()
    {
        var screen = new ScreenDefinition
        {
            ScreenId = "test",
            Identifier = new ScreenIdentifier { Row = 1, Col = 1, ExpectedText = "Test" },
            Fields = new List<FieldDefinition>
            {
                new() { Name = "secret", Type = "input", Row = 5, Col = 10, Length = 8, Attributes = "hidden" }
            }
        };

        var data = _renderer.RenderScreen(screen, new Dictionary<string, string>());

        // Find SF with non-display FFW
        bool found = false;
        for (int i = 10; i < data.Length - 2; i++)
        {
            if (data[i] == Tn5250Constants.ORDER_SF && data[i + 1] == Tn5250Constants.FFW_SHIFT_NONDISPLAY)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "Expected SF with non-display FFW for hidden field");
    }
}
