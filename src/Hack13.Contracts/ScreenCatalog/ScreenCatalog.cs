namespace Hack13.Contracts.ScreenCatalog;

public class ScreenCatalog
{
    public List<ScreenDefinition> Screens { get; set; } = new();
}

public class ScreenDefinition
{
    public string ScreenId { get; set; } = string.Empty;
    public ScreenIdentifier Identifier { get; set; } = new();
    public List<FieldDefinition> Fields { get; set; } = new();
    public List<StaticTextElement> StaticText { get; set; } = new();
}

public class ScreenIdentifier
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string ExpectedText { get; set; } = string.Empty;
}

public class FieldDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Row { get; set; }
    public int Col { get; set; }
    public int Length { get; set; }
    public string? Attributes { get; set; }
    public string? DefaultValue { get; set; }
}

public class StaticTextElement
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Text { get; set; } = string.Empty;
}
