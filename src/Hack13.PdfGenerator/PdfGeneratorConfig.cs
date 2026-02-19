namespace Hack13.PdfGenerator;

internal class PdfGeneratorConfig
{
    public string TemplateId { get; set; } = string.Empty;
    public string TemplateRegistryPath { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public string? FilenamePattern { get; set; }
    public PageConfig? Page { get; set; }
}

internal class PageConfig
{
    public string Size { get; set; } = "Letter";
    public MarginConfig? Margins { get; set; }
}

internal class MarginConfig
{
    public string Top { get; set; } = "0.75in";
    public string Bottom { get; set; } = "0.75in";
    public string Left { get; set; } = "1in";
    public string Right { get; set; } = "1in";
}

internal class TemplateRegistry
{
    public List<TemplateEntry> Templates { get; set; } = new();
}

internal class TemplateEntry
{
    public string TemplateId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public List<string> RequiredFields { get; set; } = new();
    public List<string> OptionalFields { get; set; } = new();
    public string DefaultFilenamePattern { get; set; } = "document_{{statement_date}}.pdf";
}
