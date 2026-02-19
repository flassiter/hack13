using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;

namespace Hack13.Orchestrator;

internal static class ConditionEvaluator
{
    public static bool Evaluate(ConditionDefinition condition, IReadOnlyDictionary<string, string> data)
    {
        if (condition.AllOf is { Count: > 0 })
            return condition.AllOf.All(item => Evaluate(item, data));

        if (condition.AnyOf is { Count: > 0 })
            return condition.AnyOf.Any(item => Evaluate(item, data));

        if (condition.Not != null)
            return !Evaluate(condition.Not, data);

        var key = string.IsNullOrWhiteSpace(condition.Field) ? condition.Key : condition.Field;
        key ??= string.Empty;
        var fieldValue = data.TryGetValue(key, out var value) ? value : string.Empty;
        var op = (condition.Operator ?? "equals").Trim().ToLowerInvariant();
        var expected = condition.Value ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(condition.Min) || !string.IsNullOrWhiteSpace(condition.Max))
            return EvaluateRange(fieldValue, condition.Min, condition.Max);

        return op switch
        {
            "is_empty" => string.IsNullOrWhiteSpace(fieldValue),
            "is_not_empty" => !string.IsNullOrWhiteSpace(fieldValue),
            "contains" => fieldValue.Contains(
                expected,
                condition.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            "starts_with" => fieldValue.StartsWith(
                expected,
                condition.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            "ends_with" => fieldValue.EndsWith(
                expected,
                condition.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            "not_equals" => !string.Equals(
                fieldValue,
                expected,
                condition.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            "greater_than" => CompareNumeric(fieldValue, expected, (l, r) => l > r),
            "less_than" => CompareNumeric(fieldValue, expected, (l, r) => l < r),
            "greater_than_or_equal" => CompareNumeric(fieldValue, expected, (l, r) => l >= r),
            "less_than_or_equal" => CompareNumeric(fieldValue, expected, (l, r) => l <= r),
            _ => string.Equals(
                fieldValue,
                expected,
                condition.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool EvaluateRange(string fieldValue, string? min, string? max)
    {
        if (!NumericParser.TryParse(fieldValue, out var current))
            return false;

        if (!string.IsNullOrWhiteSpace(min))
        {
            if (!NumericParser.TryParse(min, out var minValue))
                return false;
            if (current < minValue)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(max))
        {
            if (!NumericParser.TryParse(max, out var maxValue))
                return false;
            if (current > maxValue)
                return false;
        }

        return true;
    }

    private static bool CompareNumeric(string left, string right, Func<decimal, decimal, bool> compare)
    {
        if (!NumericParser.TryParse(left, out var leftValue))
            return false;
        if (!NumericParser.TryParse(right, out var rightValue))
            return false;

        return compare(leftValue, rightValue);
    }
}
