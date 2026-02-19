using System.Text.Json;
using Hack13.Calculator;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Models;

namespace Hack13.Calculator.Tests;

public class CalculatorComponentTests
{
    private static ComponentConfiguration MakeConfig(string json) => new()
    {
        ComponentType = "calculate",
        ComponentVersion = "1.0",
        Config = JsonDocument.Parse(json).RootElement
    };

    private static readonly string EscrowCalcConfig = """
        {
          "component_type": "calculate",
          "component_version": "1.0",
          "config": {
            "calculations": [
              {
                "name": "escrow_shortage",
                "output_key": "escrow_shortage_surplus",
                "operation": "subtract",
                "inputs": ["current_escrow_balance", "required_escrow_balance"],
                "format": { "decimal_places": 2 }
              },
              {
                "name": "monthly_adjustment",
                "output_key": "monthly_escrow_adjustment",
                "operation": "divide",
                "inputs": ["escrow_shortage_surplus", "12"],
                "format": { "decimal_places": 2 }
              },
              {
                "name": "adjusted_payment",
                "output_key": "adjusted_monthly_payment",
                "operation": "add",
                "inputs": ["monthly_escrow_payment", "monthly_escrow_adjustment"],
                "format": { "decimal_places": 2 }
              }
            ]
          }
        }
        """;

    // -------------------------------------------------------------------------
    // 1. Escrow calculations with known values — all 3 outputs correct
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EscrowShortageScenario_AllThreeOutputsCorrect()
    {
        var component = new CalculatorComponent();
        var config = MakeConfig(JsonDocument.Parse(EscrowCalcConfig).RootElement.GetProperty("config").ToString());
        var data = new Dictionary<string, string>
        {
            ["current_escrow_balance"] = "1200.00",
            ["required_escrow_balance"] = "1800.00",
            ["monthly_escrow_payment"] = "250.00"
        };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        // shortage = 1200 - 1800 = -600
        Assert.Equal("-600.00", data["escrow_shortage_surplus"]);
        // monthly_adjustment = -600 / 12 = -50
        Assert.Equal("-50.00", data["monthly_escrow_adjustment"]);
        // adjusted_payment = 250 + (-50) = 200
        Assert.Equal("200.00", data["adjusted_monthly_payment"]);
    }

    // -------------------------------------------------------------------------
    // 2. Surplus, deficit, and zero scenarios
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("2000.00", "1800.00", "250.00", "200.00", "16.67", "266.67")]   // surplus
    [InlineData("1200.00", "1800.00", "250.00", "-600.00", "-50.00", "200.00")] // deficit
    [InlineData("1800.00", "1800.00", "250.00", "0.00", "0.00", "250.00")]      // zero / even
    public async Task EscrowScenarios_CorrectOutputs(
        string current, string required, string monthly,
        string expectedShortage, string expectedAdj, string expectedPayment)
    {
        var component = new CalculatorComponent();
        var config = MakeConfig(JsonDocument.Parse(EscrowCalcConfig).RootElement.GetProperty("config").ToString());
        var data = new Dictionary<string, string>
        {
            ["current_escrow_balance"] = current,
            ["required_escrow_balance"] = required,
            ["monthly_escrow_payment"] = monthly
        };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal(expectedShortage, data["escrow_shortage_surplus"]);
        Assert.Equal(expectedAdj, data["monthly_escrow_adjustment"]);
        Assert.Equal(expectedPayment, data["adjusted_monthly_payment"]);
    }

    // -------------------------------------------------------------------------
    // 3. Missing input key → clear failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MissingInputKey_ReturnsFailureWithKeyName()
    {
        var component = new CalculatorComponent();
        var config = MakeConfig("""
            {
              "calculations": [{
                "name": "test_calc",
                "output_key": "result",
                "operation": "add",
                "inputs": ["missing_key"]
              }]
            }
            """);
        var data = new Dictionary<string, string>();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("MISSING_INPUT", result.Error!.ErrorCode);
        Assert.Contains("missing_key", result.Error.ErrorMessage);
    }

    // -------------------------------------------------------------------------
    // 4. Non-numeric value → clear failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NonNumericValue_ReturnsFailureWithKeyAndValue()
    {
        var component = new CalculatorComponent();
        var config = MakeConfig("""
            {
              "calculations": [{
                "name": "test_calc",
                "output_key": "result",
                "operation": "add",
                "inputs": ["bad_value_key"]
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["bad_value_key"] = "not-a-number" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("INVALID_INPUT", result.Error!.ErrorCode);
        Assert.Contains("bad_value_key", result.Error.ErrorMessage);
        Assert.Contains("not-a-number", result.Error.ErrorMessage);
    }

    // -------------------------------------------------------------------------
    // 5. Calculation chaining: second calc uses first's output
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalculationChaining_SecondUsesFirstOutput()
    {
        var component = new CalculatorComponent();
        var config = MakeConfig("""
            {
              "calculations": [
                {
                  "name": "step1",
                  "output_key": "intermediate",
                  "operation": "multiply",
                  "inputs": ["base_value", "3"],
                  "format": { "decimal_places": 2 }
                },
                {
                  "name": "step2",
                  "output_key": "final_result",
                  "operation": "add",
                  "inputs": ["intermediate", "10"],
                  "format": { "decimal_places": 2 }
                }
              ]
            }
            """);
        var data = new Dictionary<string, string> { ["base_value"] = "5.00" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("15.00", data["intermediate"]);    // 5 * 3 = 15
        Assert.Equal("25.00", data["final_result"]);    // 15 + 10 = 25
    }

    // -------------------------------------------------------------------------
    // 6. Division by zero → clear failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DivisionByZero_ReturnsFailure()
    {
        var component = new CalculatorComponent();
        var config = MakeConfig("""
            {
              "calculations": [{
                "name": "bad_divide",
                "output_key": "result",
                "operation": "divide",
                "inputs": ["numerator", "0"]
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["numerator"] = "100" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("OPERATION_ERROR", result.Error!.ErrorCode);
        Assert.Contains("zero", result.Error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Additional: All supported operations
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("add", "10", "3", "13")]
    [InlineData("subtract", "10", "3", "7")]
    [InlineData("multiply", "10", "3", "30")]
    [InlineData("divide", "10", "4", "2.5")]
    [InlineData("min", "10", "3", "3")]
    [InlineData("max", "10", "3", "10")]
    public async Task AllOperations_ProduceCorrectResults(string op, string a, string b, string expected)
    {
        var component = new CalculatorComponent();
        var config = MakeConfig($$"""
            {
              "calculations": [{
                "name": "op_test",
                "output_key": "result",
                "operation": "{{op}}",
                "inputs": ["a", "b"]
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["a"] = a, ["b"] = b };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal(expected, data["result"]);
    }

    [Fact]
    public async Task AbsOperation_NegativeInput_ReturnsPositive()
    {
        var component = new CalculatorComponent();
        var config = MakeConfig("""
            {
              "calculations": [{
                "name": "abs_test",
                "output_key": "result",
                "operation": "abs",
                "inputs": ["value"]
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["value"] = "-42.5" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("42.5", data["result"]);
    }

    [Fact]
    public async Task CurrencyInputFormat_ParsedCorrectly()
    {
        var component = new CalculatorComponent();
        var config = MakeConfig("""
            {
              "calculations": [{
                "name": "currency_test",
                "output_key": "result",
                "operation": "subtract",
                "inputs": ["balance", "required"],
                "format": { "decimal_places": 2 }
              }]
            }
            """);
        var data = new Dictionary<string, string>
        {
            ["balance"] = "$1,234.56",
            ["required"] = "$1,000.00"
        };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("234.56", data["result"]);
    }

    [Fact]
    public async Task NegativeParenthesisFormat_ParsedCorrectly()
    {
        var component = new CalculatorComponent();
        var config = MakeConfig("""
            {
              "calculations": [{
                "name": "paren_test",
                "output_key": "result",
                "operation": "abs",
                "inputs": ["value"],
                "format": { "decimal_places": 2 }
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["value"] = "(500.00)" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("500.00", data["result"]);
    }

    [Fact]
    public async Task BankersRounding_AppliedToFormat()
    {
        // 2.5 rounds to 2 (even), 3.5 rounds to 4 (even) with banker's rounding
        var component = new CalculatorComponent();
        var config = MakeConfig("""
            {
              "calculations": [
                {
                  "name": "round_half_even_down",
                  "output_key": "rounded1",
                  "operation": "add",
                  "inputs": ["val1", "0"],
                  "format": { "decimal_places": 0 }
                },
                {
                  "name": "round_half_even_up",
                  "output_key": "rounded2",
                  "operation": "add",
                  "inputs": ["val2", "0"],
                  "format": { "decimal_places": 0 }
                }
              ]
            }
            """);
        var data = new Dictionary<string, string>
        {
            ["val1"] = "2.5",
            ["val2"] = "3.5"
        };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("2", data["rounded1"]);  // 2.5 → 2 (banker's)
        Assert.Equal("4", data["rounded2"]);  // 3.5 → 4 (banker's)
    }

    [Fact]
    public async Task OutputData_ContainsOnlyWrittenKeys()
    {
        var component = new CalculatorComponent();
        var config = MakeConfig("""
            {
              "calculations": [{
                "name": "test",
                "output_key": "new_key",
                "operation": "add",
                "inputs": ["a", "b"],
                "format": { "decimal_places": 2 }
              }]
            }
            """);
        var data = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Single(result.OutputData);
        Assert.True(result.OutputData.ContainsKey("new_key"));
        Assert.DoesNotContain("a", result.OutputData.Keys);
        Assert.DoesNotContain("b", result.OutputData.Keys);
    }
}
