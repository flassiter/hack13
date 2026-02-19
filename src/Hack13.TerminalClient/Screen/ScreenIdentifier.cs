using Hack13.Contracts.Protocol;
using Hack13.Contracts.ScreenCatalog;
using Hack13.TerminalClient.Protocol;

namespace Hack13.TerminalClient.Screen;

/// <summary>
/// Identifies the current screen by checking for known text at known positions
/// using the screen catalog definitions.
/// </summary>
public class ScreenIdentifier
{
    private readonly List<ScreenDefinition> _screens;

    public ScreenIdentifier(IEnumerable<ScreenDefinition> screens)
    {
        _screens = screens.ToList();
    }

    /// <summary>
    /// Identifies the current screen by matching identifier text against the screen buffer.
    /// Returns the matching ScreenDefinition or null if no screen matches.
    /// </summary>
    public ScreenDefinition? Identify(ScreenBuffer buffer)
    {
        foreach (var screen in _screens)
        {
            var id = screen.Identifier;
            if (string.IsNullOrEmpty(id.ExpectedText))
                continue;

            var text = buffer.ReadText(id.Row, id.Col, id.ExpectedText.Length);
            if (Matches(text, id.ExpectedText))
                return screen;
        }
        return null;
    }

    /// <summary>
    /// Checks whether the buffer matches a specific screen by ID.
    /// </summary>
    public bool IsScreen(ScreenBuffer buffer, string screenId)
    {
        var screen = _screens.FirstOrDefault(s => s.ScreenId == screenId);
        if (screen == null) return false;

        var id = screen.Identifier;
        var text = buffer.ReadText(id.Row, id.Col, id.ExpectedText.Length);
        return Matches(text, id.ExpectedText);
    }

    private static bool Matches(string actual, string expected)
    {
        return string.Equals(
            actual.TrimEnd(),
            expected.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
