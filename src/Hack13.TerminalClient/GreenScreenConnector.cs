using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Interfaces;
using Hack13.Contracts.Models;
using Hack13.Contracts.ScreenCatalog;
using Hack13.Contracts.Utilities;
using Hack13.TerminalClient.Workflow;

namespace Hack13.TerminalClient;

/// <summary>
/// IComponent implementation that connects to a TN5250 green-screen system,
/// executes a scripted workflow, and returns scraped data.
/// </summary>
public class GreenScreenConnector : IComponent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string ComponentType => "green_screen_connector";

    public async Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Deserialize connector config from the component config envelope
            var connectorConfig = DeserializeConfig(config.Config);

            // Resolve placeholders in screen catalog path
            connectorConfig.ScreenCatalogPath = PlaceholderResolver.Resolve(
                connectorConfig.ScreenCatalogPath, dataDictionary);

            // Load screen catalog
            var screenDefinitions = LoadScreenCatalog(connectorConfig.ScreenCatalogPath);

            // Execute workflow
            var engine = new WorkflowEngine(connectorConfig, dataDictionary, screenDefinitions);
            return await engine.ExecuteAsync(cancellationToken);
        }
        catch (JsonException ex)
        {
            return new ComponentResult
            {
                Status = ComponentStatus.Failure,
                Error = new ComponentError
                {
                    ErrorCode = "CONFIG_ERROR",
                    ErrorMessage = $"Invalid connector configuration: {ex.Message}"
                }
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ComponentResult
            {
                Status = ComponentStatus.Failure,
                Error = new ComponentError
                {
                    ErrorCode = "UNEXPECTED_ERROR",
                    ErrorMessage = ex.Message
                }
            };
        }
    }

    private static ConnectorConfig DeserializeConfig(JsonElement configElement)
    {
        var json = configElement.GetRawText();
        return JsonSerializer.Deserialize<ConnectorConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize connector configuration");
    }

    private static List<ScreenDefinition> LoadScreenCatalog(string catalogPath)
    {
        var screens = new List<ScreenDefinition>();

        if (Directory.Exists(catalogPath))
        {
            foreach (var file in Directory.GetFiles(catalogPath, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var json = File.ReadAllText(file);
                var screen = JsonSerializer.Deserialize<ScreenDefinition>(json, JsonOptions);
                if (screen != null)
                    screens.Add(screen);
            }
        }
        else if (File.Exists(catalogPath))
        {
            var json = File.ReadAllText(catalogPath);
            var catalog = JsonSerializer.Deserialize<ScreenCatalog>(json, JsonOptions);
            if (catalog != null)
                screens.AddRange(catalog.Screens);
        }
        else
        {
            throw new FileNotFoundException($"Screen catalog not found: {catalogPath}");
        }

        ValidateScreenCatalog(screens, catalogPath);
        return screens;
    }

    private static void ValidateScreenCatalog(List<ScreenDefinition> screens, string catalogPath)
    {
        if (screens.Count == 0)
            throw new InvalidOperationException($"No screens loaded from catalog '{catalogPath}'.");

        var seenScreenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in screens)
        {
            if (string.IsNullOrWhiteSpace(screen.ScreenId))
                throw new InvalidOperationException("Screen catalog contains a screen with missing screen_id.");

            if (!seenScreenIds.Add(screen.ScreenId))
                throw new InvalidOperationException($"Duplicate screen_id '{screen.ScreenId}' in catalog.");

            if (string.IsNullOrWhiteSpace(screen.Identifier.ExpectedText))
                throw new InvalidOperationException($"Screen '{screen.ScreenId}' has empty identifier expected_text.");

            ValidateAddress(screen.ScreenId, "identifier", screen.Identifier.Row, screen.Identifier.Col);

            var seenFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in screen.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                    throw new InvalidOperationException($"Screen '{screen.ScreenId}' has a field with missing name.");

                if (!seenFieldNames.Add(field.Name))
                    throw new InvalidOperationException($"Screen '{screen.ScreenId}' has duplicate field '{field.Name}'.");

                if (field.Length <= 0)
                    throw new InvalidOperationException($"Screen '{screen.ScreenId}' field '{field.Name}' has invalid length {field.Length}.");

                ValidateAddress(screen.ScreenId, $"field '{field.Name}'", field.Row, field.Col);
            }
        }
    }

    private static void ValidateAddress(string screenId, string location, int row, int col)
    {
        if (row < 1 || row > 24 || col < 1 || col > 80)
            throw new InvalidOperationException(
                $"Screen '{screenId}' {location} has out-of-range position ({row},{col}); expected row 1-24 and col 1-80.");
    }
}
