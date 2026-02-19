namespace Hack13.Contracts.Utilities;

/// <summary>
/// Convenience extension methods for reading and writing typed values
/// from/to the workflow data dictionary (Dictionary&lt;string, string&gt;).
/// </summary>
public static class DataDictionaryExtensions
{
    public static string GetRequired(this Dictionary<string, string> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new KeyNotFoundException($"Required data dictionary key '{key}' is missing or empty.");

        return value;
    }

    public static string? GetOptional(this Dictionary<string, string> dict, string key, string? defaultValue = null)
    {
        return dict.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public static decimal GetDecimal(this Dictionary<string, string> dict, string key)
    {
        var raw = dict.GetRequired(key);
        return NumericParser.Parse(raw);
    }

    public static bool TryGetDecimal(this Dictionary<string, string> dict, string key, out decimal result)
    {
        result = 0m;
        return dict.TryGetValue(key, out var raw) && NumericParser.TryParse(raw, out result);
    }

    public static int GetInt(this Dictionary<string, string> dict, string key)
    {
        var raw = dict.GetRequired(key);
        if (!int.TryParse(raw, out var result))
            throw new FormatException($"Data dictionary key '{key}' value '{raw}' is not a valid integer.");

        return result;
    }

    public static bool GetBool(this Dictionary<string, string> dict, string key)
    {
        var raw = dict.GetRequired(key);
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw == "1"
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(this Dictionary<string, string> dict, string key, string value)
    {
        dict[key] = value;
    }

    public static void Set(this Dictionary<string, string> dict, string key, decimal value)
    {
        dict[key] = value.ToString("G");
    }

    public static void Set(this Dictionary<string, string> dict, string key, int value)
    {
        dict[key] = value.ToString();
    }

    public static void Set(this Dictionary<string, string> dict, string key, bool value)
    {
        dict[key] = value ? "true" : "false";
    }
}
