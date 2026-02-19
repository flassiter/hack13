namespace Hack13.TerminalServer.Navigation;

/// <summary>
/// Per-connection session state. Each TN5250 connection
/// has its own independent session state.
/// </summary>
public class SessionState
{
    public string CurrentScreen { get; set; } = "sign_on";
    public bool IsAuthenticated { get; set; }
    public string? UserId { get; set; }

    /// <summary>
    /// Session-scoped data dictionary. Holds values that persist
    /// across screen transitions within this session (e.g., current loan number).
    /// </summary>
    public Dictionary<string, string> Data { get; set; } = new();

    /// <summary>
    /// Gets a merged view of session data with screen-specific overrides.
    /// </summary>
    public Dictionary<string, string> GetScreenData(Dictionary<string, string>? overrides = null)
    {
        var merged = new Dictionary<string, string>(Data);
        if (overrides != null)
        {
            foreach (var kv in overrides)
                merged[kv.Key] = kv.Value;
        }
        return merged;
    }
}
