using Microsoft.Extensions.Logging;
using Hack13.Contracts.Protocol;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Navigation;

/// <summary>
/// Result of evaluating a screen transition.
/// </summary>
public class TransitionResult
{
    public bool Success { get; set; }
    public string? TargetScreen { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> DataUpdates { get; set; } = new();
}

/// <summary>
/// Evaluates navigation transitions based on current screen, AID key,
/// field values, and the navigation configuration.
/// </summary>
public class TransitionEvaluator
{
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passcode",
        "pin"
    };

    private readonly NavigationConfig _config;
    private readonly TestDataStore _testData;
    private readonly ILogger _logger;

    public TransitionEvaluator(NavigationConfig config, TestDataStore testData, ILogger logger)
    {
        _config = config;
        _testData = testData;
        _logger = logger;
    }

    public TransitionResult Evaluate(
        SessionState session,
        byte aidKey,
        Dictionary<string, string> fieldValues)
    {
        string aidName = Tn5250Constants.AidKeyName(aidKey);

        _logger.LogDebug("Evaluating transition: screen={Screen}, aid={Aid}, fields={Fields}",
            session.CurrentScreen, aidName,
            string.Join(", ", fieldValues.Select(kv => $"{kv.Key}={kv.Value}")));

        // Find matching transition rules (first match wins)
        var matchingRules = _config.Transitions
            .Where(t => t.SourceScreen == session.CurrentScreen && t.AidKey == aidName)
            .ToList();

        foreach (var rule in matchingRules)
        {
            if (!EvaluateConditions(rule.Conditions, fieldValues, session))
                continue;

            // If this rule has an error_message, it's an error transition (not a real navigation)
            if (!string.IsNullOrEmpty(rule.ErrorMessage))
            {
                return new TransitionResult
                {
                    Success = false,
                    ErrorMessage = rule.ErrorMessage
                };
            }

            // Run validation if specified
            if (rule.Validation != null)
            {
                var validationResult = RunValidation(rule.Validation, fieldValues, session);
                if (!validationResult.Success)
                {
                    return validationResult;
                }
            }

            // Transition succeeds
            var dataUpdates = new Dictionary<string, string>(rule.SetData);

            // Copy field values into session data for downstream screens,
            // excluding sensitive secrets (passwords, PINs).
            foreach (var kv in fieldValues)
            {
                if (!SensitiveFields.Contains(kv.Key))
                    dataUpdates.TryAdd(kv.Key, kv.Value);
            }

            _logger.LogInformation("Transition: {Source} --[{Aid}]--> {Target}",
                session.CurrentScreen, aidName, rule.TargetScreen);

            return new TransitionResult
            {
                Success = true,
                TargetScreen = rule.TargetScreen,
                DataUpdates = dataUpdates
            };
        }

        // No matching rule found
        return new TransitionResult
        {
            Success = false,
            ErrorMessage = $"Invalid key: {aidName}"
        };
    }

    private bool EvaluateConditions(
        Dictionary<string, string> conditions,
        Dictionary<string, string> fieldValues,
        SessionState session)
    {
        foreach (var condition in conditions)
        {
            var fieldName = condition.Key;
            var expected = condition.Value;

            // For empty/not_empty conditions, require the current input value.
            // Falling back to session state here can incorrectly accept stale data.
            var actual = fieldValues.TryGetValue(fieldName, out var fv) ? fv
                : (expected is "empty" or "not_empty") ? ""
                : session.Data.TryGetValue(fieldName, out var sv) ? sv
                : "";

            switch (expected)
            {
                case "not_empty" when string.IsNullOrWhiteSpace(actual):
                    return false;
                case "empty" when !string.IsNullOrWhiteSpace(actual):
                    return false;
                default:
                    if (expected != "not_empty" && expected != "empty" &&
                        !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                        return false;
                    break;
            }
        }
        return true;
    }

    private TransitionResult RunValidation(
        string validationType,
        Dictionary<string, string> fieldValues,
        SessionState session)
    {
        return validationType switch
        {
            "credentials" => ValidateCredentials(fieldValues),
            "loan_exists" => ValidateLoanExists(fieldValues),
            _ => new TransitionResult { Success = true }
        };
    }

    private TransitionResult ValidateCredentials(Dictionary<string, string> fieldValues)
    {
        var userId = fieldValues.GetValueOrDefault("user_id", "").Trim();
        var password = fieldValues.GetValueOrDefault("password", "").Trim();

        var valid = _config.Credentials.Any(c =>
            string.Equals(c.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
            c.Password == password);

        if (!valid)
        {
            _logger.LogWarning("Invalid credentials for user: {UserId}", userId);
            return new TransitionResult
            {
                Success = false,
                ErrorMessage = "Invalid user ID or password"
            };
        }

        return new TransitionResult { Success = true };
    }

    private TransitionResult ValidateLoanExists(Dictionary<string, string> fieldValues)
    {
        var loanNumber = fieldValues.GetValueOrDefault("loan_number", "").Trim();

        if (!_testData.TryGetLoan(loanNumber, out _))
        {
            _logger.LogWarning("Loan not found: {LoanNumber}", loanNumber);
            return new TransitionResult
            {
                Success = false,
                ErrorMessage = $"Loan {loanNumber} not found"
            };
        }

        return new TransitionResult { Success = true };
    }
}
