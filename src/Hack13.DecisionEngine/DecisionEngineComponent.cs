using System.Diagnostics;
using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Interfaces;
using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;

namespace Hack13.DecisionEngine;

public class DecisionEngineComponent : IComponent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string ComponentType => "decision";

    public Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var logs = new List<LogEntry>();

        try
        {
            var engineConfig = config.Config.Deserialize<DecisionEngineConfig>(JsonOptions)
                ?? throw new InvalidOperationException("Decision engine configuration is null.");

            var outputData = new Dictionary<string, string>();
            var mode = engineConfig.EvaluationMode.ToLowerInvariant();

            switch (mode)
            {
                case "first_match":
                    outputData = EvaluateFirstMatch(engineConfig.Rules, dataDictionary, logs);
                    break;

                case "all_match":
                    outputData = EvaluateAllMatch(engineConfig.Rules, dataDictionary, logs);
                    break;

                default:
                    return Task.FromResult(Failure(
                        "CONFIG_ERROR",
                        $"Unknown evaluation_mode '{engineConfig.EvaluationMode}'. Expected 'first_match' or 'all_match'.",
                        null, sw));
            }

            // Write matched outputs into the data dictionary
            foreach (var (key, value) in outputData)
                dataDictionary[key] = value;

            return Task.FromResult(Success(outputData, logs, sw));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(Failure("CONFIG_ERROR", $"Invalid decision engine configuration: {ex.Message}", null, sw));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(Failure("UNEXPECTED_ERROR", ex.Message, null, sw));
        }
    }

    private static Dictionary<string, string> EvaluateFirstMatch(
        List<RuleDefinition> rules,
        Dictionary<string, string> data,
        List<LogEntry> logs)
    {
        foreach (var rule in rules)
        {
            if (EvaluateCondition(rule.Condition, data))
            {
                logs.Add(MakeLog(LogLevel.Info, $"Rule '{rule.RuleName}' matched (first_match)."));
                return new Dictionary<string, string>(rule.Outputs);
            }

            logs.Add(MakeLog(LogLevel.Debug, $"Rule '{rule.RuleName}' did not match."));
        }

        logs.Add(MakeLog(LogLevel.Warn, "No rules matched in first_match mode."));
        return new Dictionary<string, string>();
    }

    private static Dictionary<string, string> EvaluateAllMatch(
        List<RuleDefinition> rules,
        Dictionary<string, string> data,
        List<LogEntry> logs)
    {
        var merged = new Dictionary<string, string>();

        foreach (var rule in rules)
        {
            if (EvaluateCondition(rule.Condition, data))
            {
                logs.Add(MakeLog(LogLevel.Info, $"Rule '{rule.RuleName}' matched (all_match)."));
                foreach (var (key, value) in rule.Outputs)
                    merged[key] = value;
            }
            else
            {
                logs.Add(MakeLog(LogLevel.Debug, $"Rule '{rule.RuleName}' did not match."));
            }
        }

        if (merged.Count == 0)
            logs.Add(MakeLog(LogLevel.Warn, "No rules matched in all_match mode."));

        return merged;
    }

    internal static bool EvaluateCondition(ConditionDefinition condition, Dictionary<string, string> data)
    {
        // Compound: AND
        if (condition.AllOf is { Count: > 0 })
            return condition.AllOf.All(c => EvaluateCondition(c, data));

        // Compound: OR
        if (condition.AnyOf is { Count: > 0 })
            return condition.AnyOf.Any(c => EvaluateCondition(c, data));

        // Compound: NOT
        if (condition.Not != null)
            return !EvaluateCondition(condition.Not, data);

        // Simple condition — missing field is treated as empty string
        var fieldValue = condition.Field != null && data.TryGetValue(condition.Field, out var v) ? v : "";

        // Range check (min/max without operator, or operator = "in_range")
        if (condition.Min != null || condition.Max != null)
            return EvaluateRange(fieldValue, condition.Min, condition.Max);

        var op = condition.Operator?.ToLowerInvariant() ?? string.Empty;

        return op switch
        {
            "is_empty" => string.IsNullOrWhiteSpace(fieldValue),
            "is_not_empty" => !string.IsNullOrWhiteSpace(fieldValue),
            "contains" => Contains(fieldValue, condition.Value ?? "", condition.CaseSensitive),
            "starts_with" => StartsWith(fieldValue, condition.Value ?? "", condition.CaseSensitive),
            "ends_with" => EndsWith(fieldValue, condition.Value ?? "", condition.CaseSensitive),
            _ => EvaluateComparison(op, fieldValue, condition.Value ?? "")
        };
    }

    private static bool EvaluateRange(string fieldValue, string? min, string? max)
    {
        if (!NumericParser.TryParse(fieldValue, out var fieldNum))
            return false;

        if (min != null)
        {
            if (!NumericParser.TryParse(min, out var minNum)) return false;
            if (fieldNum < minNum) return false;
        }

        if (max != null)
        {
            if (!NumericParser.TryParse(max, out var maxNum)) return false;
            if (fieldNum > maxNum) return false;
        }

        return true;
    }

    private static bool EvaluateComparison(string op, string fieldValue, string conditionValue)
    {
        // Try numeric comparison first for numeric-capable operators
        var numericOp = op is "equals" or "not_equals"
            or "greater_than" or "less_than"
            or "greater_than_or_equal" or "less_than_or_equal";

        if (numericOp &&
            NumericParser.TryParse(fieldValue, out var fieldNum) &&
            NumericParser.TryParse(conditionValue, out var condNum))
        {
            return op switch
            {
                "equals" => fieldNum == condNum,
                "not_equals" => fieldNum != condNum,
                "greater_than" => fieldNum > condNum,
                "less_than" => fieldNum < condNum,
                "greater_than_or_equal" => fieldNum >= condNum,
                "less_than_or_equal" => fieldNum <= condNum,
                _ => false
            };
        }

        // Fall back to string comparison for equals/not_equals
        return op switch
        {
            "equals" => string.Equals(fieldValue, conditionValue, StringComparison.OrdinalIgnoreCase),
            "not_equals" => !string.Equals(fieldValue, conditionValue, StringComparison.OrdinalIgnoreCase),
            _ => false   // numeric-only operators with non-numeric values → no match
        };
    }

    private static bool Contains(string field, string value, bool caseSensitive) =>
        field.Contains(value, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static bool StartsWith(string field, string value, bool caseSensitive) =>
        field.StartsWith(value, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static bool EndsWith(string field, string value, bool caseSensitive) =>
        field.EndsWith(value, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static ComponentResult Success(Dictionary<string, string> outputData, List<LogEntry> logs, Stopwatch sw) =>
        new()
        {
            Status = ComponentStatus.Success,
            OutputData = outputData,
            LogEntries = logs,
            DurationMs = sw.ElapsedMilliseconds
        };

    private static ComponentResult Failure(string code, string message, string? stepDetail, Stopwatch sw) =>
        new()
        {
            Status = ComponentStatus.Failure,
            Error = new ComponentError { ErrorCode = code, ErrorMessage = message, StepDetail = stepDetail },
            DurationMs = sw.ElapsedMilliseconds
        };

    private static LogEntry MakeLog(LogLevel level, string message) =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            ComponentType = "decision",
            Level = level,
            Message = message
        };
}
