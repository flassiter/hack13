namespace Hack13.DecisionEngine;

internal class DecisionEngineConfig
{
    public string EvaluationMode { get; set; } = "first_match";
    public List<RuleDefinition> Rules { get; set; } = new();
}

internal class RuleDefinition
{
    public string RuleName { get; set; } = string.Empty;
    public ConditionDefinition Condition { get; set; } = new();
    public Dictionary<string, string> Outputs { get; set; } = new();
}

internal class ConditionDefinition
{
    // Simple condition
    public string? Key { get; set; }
    public string? Field { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }

    // Range check (inclusive bounds)
    public string? Min { get; set; }
    public string? Max { get; set; }

    // String match option
    public bool CaseSensitive { get; set; } = false;

    // Compound conditions (recursive)
    public List<ConditionDefinition>? AllOf { get; set; }
    public List<ConditionDefinition>? AnyOf { get; set; }
    public ConditionDefinition? Not { get; set; }
}
