using System.Globalization;

namespace Hack13.Contracts.Utilities;

/// <summary>
/// Parses string values to decimal, handling currency symbols, commas,
/// and parentheses-as-negative notation (e.g. "(1,234.56)" → -1234.56).
/// </summary>
public static class NumericParser
{
    public static bool TryParse(string? input, out decimal result)
    {
        result = 0m;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var normalized = input.Trim();

        // Detect parentheses-as-negative: (1,234.56)
        bool isNegative = normalized.StartsWith('(') && normalized.EndsWith(')');
        if (isNegative)
            normalized = normalized[1..^1];

        // Strip currency symbols and whitespace
        normalized = normalized.Replace("$", "")
                               .Replace("£", "")
                               .Replace("€", "")
                               .Replace(",", "")
                               .Trim();

        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
            return false;

        if (isNegative)
            result = -result;

        return true;
    }

    public static decimal Parse(string? input)
    {
        if (!TryParse(input, out var result))
            throw new FormatException($"Cannot parse '{input}' as a numeric value.");

        return result;
    }
}
