using System.Text.Json;
using Hack13.Contracts.ScreenCatalog;

namespace Hack13.TerminalServer.Engine;

/// <summary>
/// Loads screen definitions from JSON files using the shared ScreenCatalog format.
/// </summary>
public class ScreenLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly Dictionary<string, ScreenDefinition> _screens = new();

    /// <summary>
    /// Loads all screen definitions from a catalog JSON file.
    /// </summary>
    public void LoadFromCatalog(string catalogPath)
    {
        var json = File.ReadAllText(catalogPath);
        var catalog = JsonSerializer.Deserialize<ScreenCatalog>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize screen catalog: {catalogPath}");

        foreach (var screen in catalog.Screens)
        {
            _screens[screen.ScreenId] = screen;
        }
    }

    /// <summary>
    /// Loads screen definitions from individual JSON files in a directory.
    /// Each file should contain a single ScreenDefinition.
    /// </summary>
    public void LoadFromDirectory(string dirPath)
    {
        foreach (var file in Directory.GetFiles(dirPath, "*.json"))
        {
            var json = File.ReadAllText(file);
            var screen = JsonSerializer.Deserialize<ScreenDefinition>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize screen: {file}");

            _screens[screen.ScreenId] = screen;
        }
    }

    public ScreenDefinition GetScreen(string screenId)
    {
        return _screens.TryGetValue(screenId, out var screen)
            ? screen
            : throw new KeyNotFoundException($"Screen not found: {screenId}");
    }

    public bool TryGetScreen(string screenId, out ScreenDefinition? screen)
    {
        return _screens.TryGetValue(screenId, out screen);
    }

    public IReadOnlyDictionary<string, ScreenDefinition> AllScreens => _screens;
}
