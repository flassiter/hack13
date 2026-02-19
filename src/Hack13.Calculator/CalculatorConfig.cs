namespace Hack13.Calculator;

internal class CalculatorConfig
{
    public List<CalculationDefinition> Calculations { get; set; } = new();
}

internal class CalculationDefinition
{
    public string Name { get; set; } = string.Empty;
    public string OutputKey { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public List<string> Inputs { get; set; } = new();
    public FormatOptions? Format { get; set; }
}

internal class FormatOptions
{
    public int? DecimalPlaces { get; set; }
}
