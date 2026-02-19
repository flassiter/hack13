using System.Text.Json;

namespace Hack13.TerminalServer.Navigation;

/// <summary>
/// Root model for the navigation configuration file.
/// </summary>
public class NavigationConfig
{
    public List<TransitionRule> Transitions { get; set; } = new();
    public List<CredentialEntry> Credentials { get; set; } = new();
    public string InitialScreen { get; set; } = "sign_on";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static NavigationConfig LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<NavigationConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize navigation config: {path}");
    }
}

/// <summary>
/// A single screen transition rule.
/// </summary>
public class TransitionRule
{
    public string SourceScreen { get; set; } = string.Empty;
    public string AidKey { get; set; } = string.Empty;
    public Dictionary<string, string> Conditions { get; set; } = new();
    public string TargetScreen { get; set; } = string.Empty;
    public string? Validation { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> SetData { get; set; } = new();
}

/// <summary>
/// Valid credential pair for the sign-on screen.
/// </summary>
public class CredentialEntry
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
