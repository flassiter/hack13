using System.Text.Json;

namespace Hack13.Contracts.Models;

public class ComponentConfiguration
{
    public string ComponentType { get; set; } = string.Empty;
    public string ComponentVersion { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonElement Config { get; set; }
}
