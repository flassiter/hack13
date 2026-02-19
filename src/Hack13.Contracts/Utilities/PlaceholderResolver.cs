using System.Text.RegularExpressions;

namespace Hack13.Contracts.Utilities;

/// <summary>
/// Resolves {{key}} placeholders in template strings from a data dictionary.
/// </summary>
public static class PlaceholderResolver
{
    private static readonly Regex PlaceholderPattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Replaces all {{key}} tokens in <paramref name="template"/> with the corresponding
    /// values from <paramref name="data"/>. Unresolved placeholders are left as-is.
    /// </summary>
    public static string Resolve(string template, IReadOnlyDictionary<string, string> data)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        return PlaceholderPattern.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return data.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// Returns all placeholder keys referenced in the template.
    /// </summary>
    public static IEnumerable<string> GetPlaceholderKeys(string template)
    {
        if (string.IsNullOrEmpty(template))
            return Enumerable.Empty<string>();

        return PlaceholderPattern.Matches(template)
            .Select(m => m.Groups[1].Value)
            .Distinct();
    }
}
