using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Models;
using Hack13.DecisionEngine;

namespace Hack13.DecisionEngine.Tests;

public class DecisionEngineComponentTests
{
    private static ComponentConfiguration MakeConfig(string configJson) => new()
    {
        ComponentType = "decision",
        ComponentVersion = "1.0",
        Config = JsonDocument.Parse(configJson).RootElement
    };

    private static readonly string EscrowRulesConfig = """
        {
          "evaluation_mode": "first_match",
          "rules": [
            {
              "rule_name": "significant_shortage",
              "condition": {
                "field": "escrow_shortage_surplus",
                "operator": "less_than",
                "value": "-500"
              },
              "outputs": {
                "notice_type": "shortage_urgent",
                "pdf_template": "escrow_shortage_urgent",
                "email_template": "escrow_shortage_urgent_email",
                "email_priority": "high"
              }
            },
            {
              "rule_name": "minor_shortage",
              "condition": {
                "field": "escrow_shortage_surplus",
                "operator": "less_than",
                "value": "0"
              },
              "outputs": {
                "notice_type": "shortage_minor",
                "pdf_template": "escrow_shortage_minor",
                "email_priority": "normal"
              }
            },
            {
              "rule_name": "surplus",
              "condition": {
                "field": "escrow_shortage_surplus",
                "operator": "greater_than",
                "value": "0"
              },
              "outputs": {
                "notice_type": "surplus",
                "pdf_template": "escrow_surplus",
                "email_priority": "normal"
              }
            },
            {
              "rule_name": "even",
              "condition": {
                "field": "escrow_shortage_surplus",
                "operator": "equals",
                "value": "0"
              },
              "outputs": {
                "notice_type": "current",
                "pdf_template": "escrow_current",
                "email_priority": "normal"
              }
            }
          ]
        }
        """;

    // -------------------------------------------------------------------------
    // 1. Each escrow rule fires with appropriate values
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("-600", "shortage_urgent", "escrow_shortage_urgent")]
    [InlineData("-100", "shortage_minor", "escrow_shortage_minor")]
    [InlineData("200",  "surplus",         "escrow_surplus")]
    [InlineData("0",    "current",         "escrow_current")]
    public async Task EscrowRules_CorrectRuleFires(string shortage, string expectedNotice, string expectedPdf)
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig(EscrowRulesConfig);
        var data = new Dictionary<string, string> { ["escrow_shortage_surplus"] = shortage };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal(expectedNotice, data["notice_type"]);
        Assert.Equal(expectedPdf, data["pdf_template"]);
    }

    // -------------------------------------------------------------------------
    // 2. first_match stops at first hit — -600 matches urgent, NOT minor
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FirstMatch_StopsAtFirstMatchedRule()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig(EscrowRulesConfig);
        // -600 satisfies both "less_than -500" and "less_than 0"
        // first_match should return "shortage_urgent" (first rule)
        var data = new Dictionary<string, string> { ["escrow_shortage_surplus"] = "-600" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("shortage_urgent", data["notice_type"]);
        Assert.Equal("high", data["email_priority"]);
        // email_template is only on the urgent rule
        Assert.True(data.ContainsKey("email_template"));
    }

    // -------------------------------------------------------------------------
    // 3. all_match accumulates outputs from every matching rule
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AllMatch_AccumulatesAllMatchingOutputs()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "all_match",
              "rules": [
                {
                  "rule_name": "rule_a",
                  "condition": { "field": "value", "operator": "greater_than", "value": "0" },
                  "outputs": { "tag_a": "yes" }
                },
                {
                  "rule_name": "rule_b",
                  "condition": { "field": "value", "operator": "less_than", "value": "100" },
                  "outputs": { "tag_b": "yes" }
                },
                {
                  "rule_name": "rule_c",
                  "condition": { "field": "value", "operator": "greater_than", "value": "50" },
                  "outputs": { "tag_c": "yes" }
                }
              ]
            }
            """);
        var data = new Dictionary<string, string> { ["value"] = "75" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        // 75 > 0 ✓, 75 < 100 ✓, 75 > 50 ✓ — all three match
        Assert.Equal("yes", data["tag_a"]);
        Assert.Equal("yes", data["tag_b"]);
        Assert.Equal("yes", data["tag_c"]);
    }

    [Fact]
    public async Task AllMatch_OnlyMatchingRulesContribute()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "all_match",
              "rules": [
                {
                  "rule_name": "rule_a",
                  "condition": { "field": "value", "operator": "greater_than", "value": "0" },
                  "outputs": { "tag_a": "yes" }
                },
                {
                  "rule_name": "rule_b",
                  "condition": { "field": "value", "operator": "greater_than", "value": "100" },
                  "outputs": { "tag_b": "yes" }
                }
              ]
            }
            """);
        var data = new Dictionary<string, string> { ["value"] = "50" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("yes", data["tag_a"]);
        Assert.False(data.ContainsKey("tag_b"));
    }

    // -------------------------------------------------------------------------
    // 4. Compound conditions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CompoundAllOf_BothMustMatch()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "and_rule",
                "condition": {
                  "all_of": [
                    { "field": "score", "operator": "greater_than_or_equal", "value": "70" },
                    { "field": "status", "operator": "equals", "value": "active" }
                  ]
                },
                "outputs": { "eligible": "true" }
              }]
            }
            """);

        // Both conditions met
        var data = new Dictionary<string, string> { ["score"] = "80", ["status"] = "active" };
        var result = await component.ExecuteAsync(config, data);
        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("true", data["eligible"]);

        // Only one condition met
        var data2 = new Dictionary<string, string> { ["score"] = "60", ["status"] = "active" };
        var result2 = await component.ExecuteAsync(config, data2);
        Assert.Equal(ComponentStatus.Success, result2.Status);
        Assert.False(data2.ContainsKey("eligible"));
    }

    [Fact]
    public async Task CompoundAnyOf_EitherSuffices()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "or_rule",
                "condition": {
                  "any_of": [
                    { "field": "vip", "operator": "equals", "value": "true" },
                    { "field": "score", "operator": "greater_than", "value": "90" }
                  ]
                },
                "outputs": { "priority": "high" }
              }]
            }
            """);

        // First condition met
        var data = new Dictionary<string, string> { ["vip"] = "true", ["score"] = "50" };
        var result = await component.ExecuteAsync(config, data);
        Assert.Equal("high", data["priority"]);

        // Second condition met
        var data2 = new Dictionary<string, string> { ["vip"] = "false", ["score"] = "95" };
        var result2 = await component.ExecuteAsync(config, data2);
        Assert.Equal("high", data2["priority"]);

        // Neither met
        var data3 = new Dictionary<string, string> { ["vip"] = "false", ["score"] = "70" };
        var result3 = await component.ExecuteAsync(config, data3);
        Assert.False(data3.ContainsKey("priority"));
    }

    [Fact]
    public async Task CompoundNot_InvertsCondition()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "not_rule",
                "condition": {
                  "not": { "field": "status", "operator": "equals", "value": "closed" }
                },
                "outputs": { "is_open": "true" }
              }]
            }
            """);

        var data = new Dictionary<string, string> { ["status"] = "active" };
        var result = await component.ExecuteAsync(config, data);
        Assert.Equal("true", data["is_open"]);

        var data2 = new Dictionary<string, string> { ["status"] = "closed" };
        var result2 = await component.ExecuteAsync(config, data2);
        Assert.False(data2.ContainsKey("is_open"));
    }

    // -------------------------------------------------------------------------
    // 5. Missing field treated as empty string
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MissingField_TreatedAsEmpty_NoError()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "empty_check",
                "condition": { "field": "missing_field", "operator": "is_empty" },
                "outputs": { "was_empty": "true" }
              }]
            }
            """);
        var data = new Dictionary<string, string>(); // no "missing_field" key

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("true", data["was_empty"]);
    }

    [Fact]
    public async Task MissingField_NumericComparison_NoMatch_NoError()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "numeric_check",
                "condition": { "field": "missing_field", "operator": "greater_than", "value": "0" },
                "outputs": { "matched": "true" }
              }]
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.False(data.ContainsKey("matched")); // no match, no error
    }

    // -------------------------------------------------------------------------
    // 6. String comparisons with case sensitivity
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StringContains_CaseInsensitiveByDefault()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "contains_check",
                "condition": {
                  "field": "description",
                  "operator": "contains",
                  "value": "URGENT"
                },
                "outputs": { "matched": "true" }
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["description"] = "This is urgent notice" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("true", data["matched"]);
    }

    [Fact]
    public async Task StringContains_CaseSensitive_NoMatch()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "cs_check",
                "condition": {
                  "field": "description",
                  "operator": "contains",
                  "value": "URGENT",
                  "case_sensitive": true
                },
                "outputs": { "matched": "true" }
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["description"] = "This is urgent notice" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.False(data.ContainsKey("matched")); // "urgent" ≠ "URGENT" with case sensitivity
    }

    [Theory]
    [InlineData("starts_with", "Hello World", "Hello", true)]
    [InlineData("starts_with", "Hello World", "World", false)]
    [InlineData("ends_with", "Hello World", "World", true)]
    [InlineData("ends_with", "Hello World", "Hello", false)]
    public async Task StringOperators_StartsWith_EndsWith(string op, string fieldValue, string condValue, bool expectMatch)
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig($$"""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "str_check",
                "condition": { "field": "text", "operator": "{{op}}", "value": "{{condValue}}" },
                "outputs": { "matched": "true" }
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["text"] = fieldValue };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal(expectMatch, data.ContainsKey("matched"));
    }

    // -------------------------------------------------------------------------
    // Additional: is_not_empty, numeric operators, range check
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsNotEmpty_FieldPresent_Matches()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "not_empty",
                "condition": { "field": "name", "operator": "is_not_empty" },
                "outputs": { "has_name": "true" }
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["name"] = "John" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal("true", data["has_name"]);
    }

    [Fact]
    public async Task RangeCheck_FieldWithinBounds_Matches()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "range_rule",
                "condition": { "field": "score", "min": "60", "max": "80" },
                "outputs": { "grade": "B" }
              }]
            }
            """);

        var dataIn = new Dictionary<string, string> { ["score"] = "70" };
        var result = await component.ExecuteAsync(config, dataIn);
        Assert.Equal("B", dataIn["grade"]);

        var dataOut = new Dictionary<string, string> { ["score"] = "90" };
        var result2 = await component.ExecuteAsync(config, dataOut);
        Assert.False(dataOut.ContainsKey("grade"));
    }

    [Fact]
    public async Task NoRulesMatch_FirstMatch_SuccessWithNoOutput()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "never_fires",
                "condition": { "field": "x", "operator": "equals", "value": "impossible" },
                "outputs": { "flag": "set" }
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["x"] = "something_else" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Empty(result.OutputData);
    }

    [Fact]
    public async Task OutputsWrittenToDataDictionary()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig(EscrowRulesConfig);
        var data = new Dictionary<string, string> { ["escrow_shortage_surplus"] = "-600" };

        var result = await component.ExecuteAsync(config, data);

        // Outputs must be in both result.OutputData and the shared data dictionary
        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.True(result.OutputData.ContainsKey("notice_type"));
        Assert.Equal("shortage_urgent", data["notice_type"]);
        Assert.Equal(result.OutputData["notice_type"], data["notice_type"]);
    }

    [Fact]
    public async Task MissingOperator_DefaultsToEquals()
    {
        var component = new DecisionEngineComponent();
        var config = MakeConfig("""
            {
              "evaluation_mode": "first_match",
              "rules": [{
                "rule_name": "default_equals",
                "condition": { "field": "status", "value": "ready" },
                "outputs": { "matched": "true" }
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["status"] = "ready" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("true", data["matched"]);
    }
}
