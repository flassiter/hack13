using Hack13.Contracts.Protocol;
using Hack13.TerminalClient.Protocol;

namespace Hack13.TerminalClient.Tests;

public class ScreenBufferTests
{
    [Fact]
    public void New_buffer_is_filled_with_spaces()
    {
        var buf = new ScreenBuffer();
        for (int r = 1; r <= 24; r++)
            for (int c = 1; c <= 80; c++)
                Assert.Equal(' ', buf.GetChar(r, c));
    }

    [Fact]
    public void SetChar_and_GetChar_roundtrip()
    {
        var buf = new ScreenBuffer();
        buf.SetChar(5, 10, 'X');
        Assert.Equal('X', buf.GetChar(5, 10));
    }

    [Fact]
    public void ReadText_returns_substring_at_position()
    {
        var buf = new ScreenBuffer();
        var text = "Hello World";
        for (int i = 0; i < text.Length; i++)
            buf.SetChar(3, 5 + i, text[i]);

        Assert.Equal("Hello World", buf.ReadText(3, 5, 11));
    }

    [Fact]
    public void ReadText_pads_with_spaces_for_unset_positions()
    {
        var buf = new ScreenBuffer();
        buf.SetChar(1, 1, 'A');
        var result = buf.ReadText(1, 1, 5);
        Assert.Equal("A    ", result);
    }

    [Fact]
    public void ReadRow_returns_full_80_char_row()
    {
        var buf = new ScreenBuffer();
        buf.SetChar(1, 1, 'X');
        buf.SetChar(1, 80, 'Y');
        var row = buf.ReadRow(1);
        Assert.Equal(80, row.Length);
        Assert.Equal('X', row[0]);
        Assert.Equal('Y', row[79]);
    }

    [Fact]
    public void Clear_resets_grid_and_fields()
    {
        var buf = new ScreenBuffer();
        buf.SetChar(1, 1, 'Z');
        buf.AddField(new ScreenField { Row = 1, Col = 1, Length = 5 });
        Assert.Single(buf.Fields);

        buf.Clear();
        Assert.Equal(' ', buf.GetChar(1, 1));
        Assert.Empty(buf.Fields);
    }

    [Fact]
    public void FillRange_fills_characters()
    {
        var buf = new ScreenBuffer();
        buf.FillRange(1, 1, 1, 5, '-');
        Assert.Equal("----", buf.ReadText(1, 1, 4));
    }

    [Fact]
    public void AddField_and_GetInputFields_work()
    {
        var buf = new ScreenBuffer();
        buf.AddField(new ScreenField { Row = 10, Col = 35, Length = 10, Ffw0 = 0x00, Ffw1 = 0x00 });
        buf.AddField(new ScreenField { Row = 12, Col = 35, Length = 10, Ffw0 = Tn5250Constants.FFW_BYPASS, Ffw1 = 0x00 });

        var inputs = buf.GetInputFields().ToList();
        Assert.Single(inputs);
        Assert.Equal(10, inputs[0].Row);
    }

    [Fact]
    public void FindInputField_returns_correct_field()
    {
        var buf = new ScreenBuffer();
        buf.AddField(new ScreenField { Row = 10, Col = 35, Length = 10, Ffw0 = 0x00, Ffw1 = 0x00 });

        var found = buf.FindInputField(10, 35);
        Assert.NotNull(found);
        Assert.Equal(10, found.Length);
    }

    [Fact]
    public void FindInputField_returns_null_for_protected_field()
    {
        var buf = new ScreenBuffer();
        buf.AddField(new ScreenField { Row = 10, Col = 35, Length = 10, Ffw0 = Tn5250Constants.FFW_BYPASS, Ffw1 = 0x00 });

        var found = buf.FindInputField(10, 35);
        Assert.Null(found);
    }

    [Fact]
    public void ScreenField_IsHidden_detects_nondisplay_shift()
    {
        var field = new ScreenField { Ffw0 = Tn5250Constants.FFW_SHIFT_NONDISPLAY };
        Assert.True(field.IsHidden);

        var normalField = new ScreenField { Ffw0 = Tn5250Constants.FFW_SHIFT_ALPHA };
        Assert.False(normalField.IsHidden);
    }
}
