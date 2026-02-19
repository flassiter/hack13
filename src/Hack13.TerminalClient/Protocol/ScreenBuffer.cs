using Hack13.Contracts.Protocol;

namespace Hack13.TerminalClient.Protocol;

/// <summary>
/// Metadata for a field discovered during screen parsing.
/// </summary>
public class ScreenField
{
    public int Row { get; set; }
    public int Col { get; set; }
    public int Length { get; set; }
    public byte Ffw0 { get; set; }
    public byte Ffw1 { get; set; }

    public bool IsProtected => (Ffw0 & Tn5250Constants.FFW_BYPASS) != 0;
    public bool IsHidden => (Ffw0 & 0x07) == Tn5250Constants.FFW_SHIFT_NONDISPLAY;
    public bool IsInput => !IsProtected;
}

/// <summary>
/// Internal representation of the current TN5250 screen.
/// Maintains a 24x80 character grid and field metadata map.
/// </summary>
public class ScreenBuffer
{
    private readonly char[,] _grid;
    private readonly List<ScreenField> _fields = new();

    public int Rows { get; } = Tn5250Constants.SCREEN_ROWS;
    public int Cols { get; } = Tn5250Constants.SCREEN_COLS;
    public int CursorRow { get; set; } = 1;
    public int CursorCol { get; set; } = 1;
    public IReadOnlyList<ScreenField> Fields => _fields;

    public ScreenBuffer()
    {
        _grid = new char[Rows + 1, Cols + 1]; // 1-based indexing
        Clear();
    }

    public void Clear()
    {
        for (int r = 1; r <= Rows; r++)
            for (int c = 1; c <= Cols; c++)
                _grid[r, c] = ' ';
        _fields.Clear();
    }

    /// <summary>
    /// Sets a character at the given 1-based row/col position.
    /// </summary>
    public void SetChar(int row, int col, char ch)
    {
        if (row >= 1 && row <= Rows && col >= 1 && col <= Cols)
            _grid[row, col] = ch;
    }

    /// <summary>
    /// Gets a character at the given 1-based row/col position.
    /// </summary>
    public char GetChar(int row, int col)
    {
        if (row >= 1 && row <= Rows && col >= 1 && col <= Cols)
            return _grid[row, col];
        return ' ';
    }

    /// <summary>
    /// Reads a substring from the screen at the given position.
    /// </summary>
    public string ReadText(int row, int col, int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            int c = col + i;
            if (c > Cols)
            {
                // Wrap to next row
                row++;
                c = 1 + (c - Cols - 1);
            }
            chars[i] = (row <= Rows) ? _grid[row, c] : ' ';
        }
        return new string(chars);
    }

    /// <summary>
    /// Reads a full screen row (1-based).
    /// </summary>
    public string ReadRow(int row)
    {
        return ReadText(row, 1, Cols);
    }

    /// <summary>
    /// Adds field metadata discovered during parsing.
    /// </summary>
    public void AddField(ScreenField field)
    {
        _fields.Add(field);
    }

    /// <summary>
    /// Finds an input field at the given row/col (where the SF attribute byte sits).
    /// </summary>
    public ScreenField? FindInputField(int row, int col)
    {
        return _fields.FirstOrDefault(f => f.IsInput && f.Row == row && f.Col == col);
    }

    /// <summary>
    /// Gets all input (non-protected) fields.
    /// </summary>
    public IEnumerable<ScreenField> GetInputFields()
    {
        return _fields.Where(f => f.IsInput);
    }

    /// <summary>
    /// Fills a range of positions with a character.
    /// </summary>
    public void FillRange(int fromRow, int fromCol, int toRow, int toCol, char ch)
    {
        int pos = (fromRow - 1) * Cols + (fromCol - 1);
        int endPos = (toRow - 1) * Cols + (toCol - 1);

        while (pos < endPos)
        {
            int r = (pos / Cols) + 1;
            int c = (pos % Cols) + 1;
            if (r >= 1 && r <= Rows && c >= 1 && c <= Cols)
                _grid[r, c] = ch;
            pos++;
        }
    }
}
