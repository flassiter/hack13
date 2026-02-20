using System.Text.Json;

namespace Hack13.Contracts.Utilities;

public static class SimpleJsonPath
{
    public static bool TryExtract(string json, string path, out string? value, out string? error)
    {
        value = null;
        error = null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }

        using (doc)
        {
            var normalizedPath = path.StartsWith("$.", StringComparison.Ordinal)
                ? path[2..]
                : path.TrimStart('$').TrimStart('.');

            if (string.IsNullOrEmpty(normalizedPath))
            {
                value = doc.RootElement.GetRawText();
                return true;
            }

            var current = doc.RootElement;
            foreach (var segment in normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var bracketPos = segment.IndexOf('[');
                if (bracketPos >= 0)
                {
                    var propName = segment[..bracketPos];
                    var endBracket = segment.IndexOf(']', bracketPos);
                    if (endBracket < 0)
                    {
                        error = $"Invalid JSON path segment '{segment}': missing ']'.";
                        return false;
                    }

                    if (!int.TryParse(segment[(bracketPos + 1)..endBracket], out var idx) || idx < 0)
                    {
                        error = $"Invalid array index in JSON path segment '{segment}'.";
                        return false;
                    }

                    if (!string.IsNullOrEmpty(propName))
                    {
                        if (!current.TryGetProperty(propName, out current))
                            return true;
                    }

                    if (current.ValueKind != JsonValueKind.Array || idx >= current.GetArrayLength())
                        return true;

                    current = current[idx];
                    continue;
                }

                if (!current.TryGetProperty(segment, out current))
                    return true;
            }

            value = current.ValueKind == JsonValueKind.String
                ? current.GetString()
                : current.GetRawText();
            return true;
        }
    }
}
