using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Interfaces;
using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;

namespace Hack13.Calculator;

public class CalculatorComponent : IComponent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string ComponentType => "calculate";

    public Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var logs = new List<LogEntry>();
        var writtenKeys = new Dictionary<string, string>();

        try
        {
            var calcConfig = config.Config.Deserialize<CalculatorConfig>(JsonOptions)
                ?? throw new InvalidOperationException("Calculator configuration is null.");

            if (calcConfig.Calculations.Count == 0)
            {
                logs.Add(MakeLog(LogLevel.Warn, "No calculations defined."));
                return Task.FromResult(Success(writtenKeys, logs, sw));
            }

            foreach (var calc in calcConfig.Calculations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                logs.Add(MakeLog(LogLevel.Debug, $"Executing '{calc.Name}' (op={calc.Operation})"));

                var inputs = ResolveInputs(calc, dataDictionary, out var resolveError);
                if (resolveError.HasValue)
                    return Task.FromResult(Failure(resolveError.Value.Code, resolveError.Value.Message, calc.Name, sw));

                if (!TryExecuteOperation(calc.Operation, inputs!, out var result, out var opError))
                    return Task.FromResult(Failure("OPERATION_ERROR", $"Calculation '{calc.Name}': {opError}", calc.Name, sw));

                var formatted = FormatResult(result, calc.Format);
                dataDictionary[calc.OutputKey] = formatted;
                writtenKeys[calc.OutputKey] = formatted;

                logs.Add(MakeLog(LogLevel.Info, $"'{calc.Name}' → {formatted} written to '{calc.OutputKey}'"));
            }

            return Task.FromResult(Success(writtenKeys, logs, sw));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(Failure("CONFIG_ERROR", $"Invalid calculator configuration: {ex.Message}", null, sw));
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

    private static List<decimal>? ResolveInputs(
        CalculationDefinition calc,
        Dictionary<string, string> dataDictionary,
        out (string Code, string Message)? error)
    {
        error = null;
        var results = new List<decimal>();

        foreach (var input in calc.Inputs)
        {
            // Treat as literal if the string itself is a valid number
            if (IsNumericLiteral(input))
            {
                NumericParser.TryParse(input, out var literal);
                results.Add(literal);
                continue;
            }

            // Look up key in data dictionary
            if (!dataDictionary.TryGetValue(input, out var rawValue))
            {
                error = ("MISSING_INPUT",
                    $"Calculation '{calc.Name}': input key '{input}' not found in data dictionary.");
                return null;
            }

            if (!NumericParser.TryParse(rawValue, out var parsed))
            {
                error = ("INVALID_INPUT",
                    $"Calculation '{calc.Name}': input key '{input}' has non-numeric value '{rawValue}'.");
                return null;
            }

            results.Add(parsed);
        }

        return results;
    }

    // A plain decimal string (no letters) is treated as a literal, not a key.
    private static bool IsNumericLiteral(string input) =>
        decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    private static bool TryExecuteOperation(
        string operation,
        List<decimal> inputs,
        out decimal result,
        out string? error)
    {
        error = null;
        result = 0m;

        switch (operation.ToLowerInvariant())
        {
            case "add":
                result = inputs.Aggregate(0m, (acc, x) => acc + x);
                return true;

            case "subtract":
                if (inputs.Count < 2) { error = $"'subtract' requires at least 2 inputs, got {inputs.Count}."; return false; }
                result = inputs[0];
                foreach (var v in inputs.Skip(1)) result -= v;
                return true;

            case "multiply":
                result = inputs.Aggregate(1m, (acc, x) => acc * x);
                return true;

            case "divide":
                if (inputs.Count != 2) { error = $"'divide' requires exactly 2 inputs, got {inputs.Count}."; return false; }
                if (inputs[1] == 0m) { error = "Division by zero."; return false; }
                result = inputs[0] / inputs[1];
                return true;

            case "round":
                if (inputs.Count < 1) { error = "'round' requires at least 1 input."; return false; }
                var places = inputs.Count >= 2 ? (int)inputs[1] : 0;
                result = Math.Round(inputs[0], places, MidpointRounding.ToEven);
                return true;

            case "min":
                if (inputs.Count == 0) { error = "'min' requires at least 1 input."; return false; }
                result = inputs.Min();
                return true;

            case "max":
                if (inputs.Count == 0) { error = "'max' requires at least 1 input."; return false; }
                result = inputs.Max();
                return true;

            case "abs":
                if (inputs.Count < 1) { error = "'abs' requires at least 1 input."; return false; }
                result = Math.Abs(inputs[0]);
                return true;

            default:
                error = $"Unknown operation '{operation}'.";
                return false;
        }
    }

    private static string FormatResult(decimal value, FormatOptions? format)
    {
        if (format?.DecimalPlaces.HasValue == true)
        {
            var rounded = Math.Round(value, format.DecimalPlaces.Value, MidpointRounding.ToEven);
            return rounded.ToString($"F{format.DecimalPlaces.Value}", CultureInfo.InvariantCulture);
        }

        return value.ToString("G", CultureInfo.InvariantCulture);
    }

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
            ComponentType = "calculate",
            Level = level,
            Message = message
        };
}
