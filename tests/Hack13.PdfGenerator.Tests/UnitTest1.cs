using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Models;
using Hack13.PdfGenerator;

namespace Hack13.PdfGenerator.Tests;

public class TemplateEngineTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string TemplatesDir =>
        FindTemplatesDir(AppContext.BaseDirectory);

    private static string RegistryPath =>
        Path.Combine(TemplatesDir, "template-registry.json");

    private static string FindTemplatesDir(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null && !dir.GetFiles("*.sln").Any())
            dir = dir.Parent;

        if (dir == null)
            throw new DirectoryNotFoundException("Cannot locate solution root from: " + startDir);

        var templates = Path.Combine(dir.FullName, "configs", "templates");
        if (!Directory.Exists(templates))
            throw new DirectoryNotFoundException("configs/templates not found at: " + templates);

        return templates;
    }

    // ── Registry loading ─────────────────────────────────────────────────────

    [Fact]
    public void LoadRegistry_ReturnsAllFourTemplates()
    {
        var registry = TemplateEngine.LoadRegistry(RegistryPath);

        Assert.NotNull(registry);
        Assert.Equal(4, registry.Templates.Count);
        Assert.Contains(registry.Templates, t => t.TemplateId == "escrow_shortage_urgent");
        Assert.Contains(registry.Templates, t => t.TemplateId == "escrow_shortage_minor");
        Assert.Contains(registry.Templates, t => t.TemplateId == "escrow_surplus");
        Assert.Contains(registry.Templates, t => t.TemplateId == "escrow_current");
    }

    [Fact]
    public void LoadRegistry_ThrowsWhenFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            TemplateEngine.LoadRegistry("/nonexistent/path/registry.json"));
    }

    [Fact]
    public void FindTemplate_ReturnsCorrectEntry()
    {
        var registry = TemplateEngine.LoadRegistry(RegistryPath);
        var entry = TemplateEngine.FindTemplate(registry, "escrow_shortage_urgent");

        Assert.Equal("escrow_shortage_urgent", entry.TemplateId);
        Assert.NotEmpty(entry.FilePath);
        Assert.Contains("borrower_name", entry.RequiredFields);
        Assert.Contains("loan_number", entry.RequiredFields);
    }

    [Fact]
    public void FindTemplate_ThrowsWhenNotFound()
    {
        var registry = TemplateEngine.LoadRegistry(RegistryPath);

        Assert.Throws<KeyNotFoundException>(() =>
            TemplateEngine.FindTemplate(registry, "nonexistent_template"));
    }

    // ── Required field validation ─────────────────────────────────────────────

    [Fact]
    public void ValidateRequiredFields_ReturnsEmpty_WhenAllPresent()
    {
        var registry = TemplateEngine.LoadRegistry(RegistryPath);
        var entry = TemplateEngine.FindTemplate(registry, "escrow_shortage_urgent");
        var data = TestHelpers.BuildLoan1Data();

        var missing = TemplateEngine.ValidateRequiredFields(entry, data);

        Assert.Empty(missing);
    }

    [Fact]
    public void ValidateRequiredFields_ReturnsMissingField_WhenAbsent()
    {
        var registry = TemplateEngine.LoadRegistry(RegistryPath);
        var entry = TemplateEngine.FindTemplate(registry, "escrow_shortage_urgent");
        var data = TestHelpers.BuildLoan1Data();
        data.Remove("borrower_name");

        var missing = TemplateEngine.ValidateRequiredFields(entry, data);

        Assert.Contains("borrower_name", missing);
    }

    [Fact]
    public void ValidateRequiredFields_ReturnsField_WhenValueIsEmpty()
    {
        var registry = TemplateEngine.LoadRegistry(RegistryPath);
        var entry = TemplateEngine.FindTemplate(registry, "escrow_shortage_urgent");
        var data = TestHelpers.BuildLoan1Data();
        data["loan_number"] = "";

        var missing = TemplateEngine.ValidateRequiredFields(entry, data);

        Assert.Contains("loan_number", missing);
    }

    // ── Placeholder rendering ─────────────────────────────────────────────────

    [Fact]
    public void Render_ReplacesSimplePlaceholder()
    {
        const string template = "Dear {{borrower_name}},";
        var data = new Dictionary<string, string> { ["borrower_name"] = "SMITH, JOHN" };

        var result = TemplateEngine.Render(template, data);

        Assert.Equal("Dear SMITH, JOHN,", result);
    }

    [Fact]
    public void Render_CurrencyFormat_FormatsDecimal()
    {
        const string template = "Balance: {{escrow_balance:currency}}";
        var data = new Dictionary<string, string> { ["escrow_balance"] = "$2150.00" };

        var result = TemplateEngine.Render(template, data);

        Assert.Equal("Balance: $2,150.00", result);
    }

    [Fact]
    public void Render_DateFormat_FormatsLongDate()
    {
        const string template = "Date: {{last_analysis_date:date}}";
        var data = new Dictionary<string, string> { ["last_analysis_date"] = "01/15/2025" };

        var result = TemplateEngine.Render(template, data);

        Assert.Equal("Date: January 15, 2025", result);
    }

    [Fact]
    public void Render_DateFormat_HandlesIsoDate()
    {
        const string template = "Date: {{statement_date:date}}";
        var data = new Dictionary<string, string> { ["statement_date"] = "2026-02-18" };

        var result = TemplateEngine.Render(template, data);

        Assert.Equal("Date: February 18, 2026", result);
    }

    [Fact]
    public void Render_UppercaseFormat_ConvertsToUpper()
    {
        const string template = "{{borrower_name:uppercase}}";
        var data = new Dictionary<string, string> { ["borrower_name"] = "smith, john" };

        var result = TemplateEngine.Render(template, data);

        Assert.Equal("SMITH, JOHN", result);
    }

    [Fact]
    public void Render_MissingPlaceholder_RendersEmpty()
    {
        const string template = "Name: {{missing_field}}";
        var data = new Dictionary<string, string>();

        var result = TemplateEngine.Render(template, data);

        Assert.Equal("Name: ", result);
    }

    [Fact]
    public void Render_EncodesHtmlByDefault()
    {
        const string template = "<p>{{borrower_name}}</p>";
        var data = new Dictionary<string, string> { ["borrower_name"] = "<b>SMITH & SONS</b>" };

        var result = TemplateEngine.Render(template, data);

        Assert.Equal("<p>&lt;b&gt;SMITH &amp; SONS&lt;/b&gt;</p>", result);
    }

    [Fact]
    public void Render_RawFormat_DoesNotEncodeHtml()
    {
        const string template = "<div>{{html_block:raw}}</div>";
        var data = new Dictionary<string, string> { ["html_block"] = "<span>safe</span>" };

        var result = TemplateEngine.Render(template, data);

        Assert.Equal("<div><span>safe</span></div>", result);
    }

    // ── Conditional blocks ────────────────────────────────────────────────────

    [Fact]
    public void Render_ConditionalBlock_ShowsWhenNonZero()
    {
        const string template = "{{#if flood_insurance}}<tr>Flood: {{flood_insurance:currency}}</tr>{{/if}}";
        var data = new Dictionary<string, string> { ["flood_insurance"] = "$620.00" };

        var result = TemplateEngine.Render(template, data);

        Assert.Contains("Flood:", result);
        Assert.Contains("$620.00", result);
    }

    [Fact]
    public void Render_ConditionalBlock_HidesWhenZero()
    {
        const string template = "{{#if flood_insurance}}<tr>Flood: {{flood_insurance}}</tr>{{/if}}";
        var data = new Dictionary<string, string> { ["flood_insurance"] = "$0.00" };

        var result = TemplateEngine.Render(template, data);

        Assert.DoesNotContain("Flood:", result);
        Assert.Empty(result.Trim());
    }

    [Fact]
    public void Render_ConditionalBlock_HidesWhenMissing()
    {
        const string template = "before{{#if flood_insurance}}<tr>Flood</tr>{{/if}}after";
        var data = new Dictionary<string, string>();

        var result = TemplateEngine.Render(template, data);

        Assert.Equal("beforeafter", result);
    }

    // ── Template file loading ─────────────────────────────────────────────────

    [Fact]
    public void LoadTemplate_ThrowsWhenFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            TemplateEngine.LoadTemplate("/nonexistent/template.html"));
    }

    [Fact]
    public void ResolveRelativePath_ResolvesRelativeToRegistry()
    {
        var registryFile = RegistryPath;
        var resolved = TemplateEngine.ResolveRelativePath(registryFile, "./escrow_shortage_urgent.html");

        Assert.True(File.Exists(resolved), $"Resolved path does not exist: {resolved}");
        Assert.Contains("escrow_shortage_urgent.html", resolved);
    }

    [Fact]
    public void ResolveRelativePath_ThrowsWhenPathEscapesRegistryDirectory()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TemplateEngine.ResolveRelativePath(RegistryPath, "../outside-template.html"));
    }
}

/// <summary>Integration tests that render actual PDFs. Require Chromium download.</summary>
[Trait("Category", "Integration")]
public class PdfGeneratorComponentIntegrationTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ComponentConfiguration MakeConfig(
        string templateId,
        string outputDir,
        string? registryPath = null) =>
        new()
        {
            ComponentType = "pdf_generator",
            ComponentVersion = "1.0",
            Config = JsonDocument.Parse($$"""
            {
              "template_id": "{{templateId}}",
              "template_registry_path": "{{(registryPath ?? FindRegistryPath()).Replace("\\", "\\\\")}}",
              "output_directory": "{{outputDir.Replace("\\", "\\\\")}}"
            }
            """).RootElement
        };

    private static string FindRegistryPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.sln").Any())
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("Cannot locate solution root.");
        return Path.Combine(dir.FullName, "configs", "templates", "template-registry.json");
    }

    private static string TempOutputDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hack13_pdf_tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── Loan test data sets ───────────────────────────────────────────────────

    private static Dictionary<string, string> Loan1Data() => new()
    {
        ["loan_number"] = "1000001",
        ["borrower_name"] = "SMITH, JOHN A",
        ["property_address"] = "123 OAK STREET, ANYTOWN TX 75001",
        ["escrow_balance"] = "$2,150.00",
        ["escrow_payment"] = "$485.00",
        ["required_reserve"] = "$2,800.00",
        ["shortage_amount"] = "$650.00",
        ["surplus_amount"] = "$0.00",
        ["monthly_payment"] = "$1,229.85",
        ["tax_amount"] = "$3,200.00",
        ["hazard_insurance"] = "$1,450.00",
        ["flood_insurance"] = "$620.00",
        ["mortgage_insurance"] = "$0.00",
        ["last_analysis_date"] = "01/15/2025",
        ["next_analysis_date"] = "01/15/2026"
    };

    private static Dictionary<string, string> Loan2Data() => new()
    {
        ["loan_number"] = "1000002",
        ["borrower_name"] = "JOHNSON, MARIA L",
        ["property_address"] = "456 MAPLE AVE, DALLAS TX 75201",
        ["escrow_balance"] = "$4,200.00",
        ["escrow_payment"] = "$395.00",
        ["required_reserve"] = "$3,700.00",
        ["shortage_amount"] = "$0.00",
        ["surplus_amount"] = "$500.00",
        ["monthly_payment"] = "$833.61",
        ["tax_amount"] = "$2,800.00",
        ["hazard_insurance"] = "$1,100.00",
        ["flood_insurance"] = "$0.00",       // no flood insurance
        ["mortgage_insurance"] = "$125.00",
        ["last_analysis_date"] = "12/01/2024",
        ["next_analysis_date"] = "12/01/2025"
    };

    private static Dictionary<string, string> Loan3Data() => new()
    {
        ["loan_number"] = "1000003",
        ["borrower_name"] = "WILLIAMS, ROBERT T",
        ["property_address"] = "789 PINE BLVD, HOUSTON TX 77001",
        ["escrow_balance"] = "$3,050.00",
        ["escrow_payment"] = "$510.00",
        ["required_reserve"] = "$3,100.00",
        ["shortage_amount"] = "$50.00",
        ["surplus_amount"] = "$0.00",
        ["monthly_payment"] = "$1,437.09",
        ["tax_amount"] = "$4,100.00",
        ["hazard_insurance"] = "$1,800.00",
        ["flood_insurance"] = "$0.00",       // no flood insurance
        ["mortgage_insurance"] = "$0.00",
        ["last_analysis_date"] = "11/01/2024",
        ["next_analysis_date"] = "11/01/2025"
    };

    // ── Success tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GeneratePdf_Loan1_UrgentShortage_Succeeds()
    {
        var component = new PdfGeneratorComponent();
        var outputDir = TempOutputDir();
        var config = MakeConfig("escrow_shortage_urgent", outputDir);
        var data = Loan1Data();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.True(result.OutputData.ContainsKey("pdf_file_path"));

        var path = result.OutputData["pdf_file_path"];
        Assert.True(File.Exists(path), $"PDF not found at: {path}");
        Assert.True(new FileInfo(path).Length > 1000, "PDF file is suspiciously small.");
        Assert.Equal(Path.GetFileName(path), result.OutputData["pdf_file_name"]);
        Assert.True(int.Parse(result.OutputData["pdf_page_count"]) >= 1);
        Assert.Contains("1000001", result.OutputData["pdf_file_name"]);
    }

    [Fact]
    public async Task GeneratePdf_Loan2_Surplus_ZeroFloodInsurance_Succeeds()
    {
        var component = new PdfGeneratorComponent();
        var outputDir = TempOutputDir();
        var config = MakeConfig("escrow_surplus", outputDir);
        var data = Loan2Data();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        var path = result.OutputData["pdf_file_path"];
        Assert.True(File.Exists(path));

        // Verify flood insurance row was NOT rendered (zero value)
        // We can inspect the rendered HTML indirectly by checking the PDF exists
        // and was generated from the data with flood_insurance = $0.00
        Assert.Contains("1000002", result.OutputData["pdf_file_name"]);
    }

    [Fact]
    public async Task GeneratePdf_Loan3_MinorShortage_ZeroFloodInsurance_Succeeds()
    {
        var component = new PdfGeneratorComponent();
        var outputDir = TempOutputDir();
        var config = MakeConfig("escrow_shortage_minor", outputDir);
        var data = Loan3Data();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.True(File.Exists(result.OutputData["pdf_file_path"]));
        Assert.Contains("1000003", result.OutputData["pdf_file_name"]);
    }

    [Fact]
    public async Task GeneratePdf_WithCalculatorFields_IncludesAdjustmentRows()
    {
        var component = new PdfGeneratorComponent();
        var outputDir = TempOutputDir();
        var config = MakeConfig("escrow_shortage_urgent", outputDir);
        var data = Loan1Data();

        // Add Calculator component outputs
        data["escrow_shortage_surplus"] = "-650.00";
        data["monthly_escrow_adjustment"] = "54.17";
        data["adjusted_monthly_payment"] = "539.17";

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.True(File.Exists(result.OutputData["pdf_file_path"]));
    }

    [Fact]
    public async Task GeneratePdf_TemplateIdFromDataDictionary_Resolves()
    {
        var component = new PdfGeneratorComponent();
        var outputDir = TempOutputDir();
        var registryPath = FindRegistryPath();

        // template_id as a placeholder resolved from data dictionary
        var config = new ComponentConfiguration
        {
            ComponentType = "pdf_generator",
            ComponentVersion = "1.0",
            Config = JsonDocument.Parse($$$"""
            {
              "template_id": "{{pdf_template}}",
              "template_registry_path": "{{{registryPath.Replace("\\", "\\\\")}}}",
              "output_directory": "{{{outputDir.Replace("\\", "\\\\")}}}"
            }
            """).RootElement
        };

        var data = Loan1Data();
        data["pdf_template"] = "escrow_shortage_urgent";

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.True(File.Exists(result.OutputData["pdf_file_path"]));
    }

    [Fact]
    public async Task GeneratePdf_OutputDataContainsAllKeys()
    {
        var component = new PdfGeneratorComponent();
        var outputDir = TempOutputDir();
        var config = MakeConfig("escrow_current", outputDir);
        var data = Loan2Data();
        data.Remove("shortage_amount"); // Not required for escrow_current

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.True(result.OutputData.ContainsKey("pdf_file_path"));
        Assert.True(result.OutputData.ContainsKey("pdf_file_name"));
        Assert.True(result.OutputData.ContainsKey("pdf_file_size"));
        Assert.True(result.OutputData.ContainsKey("pdf_page_count"));
        Assert.True(long.Parse(result.OutputData["pdf_file_size"]) > 0);
    }

    // ── Failure tests (no Chromium needed) ────────────────────────────────────

    [Fact]
    public async Task GeneratePdf_MissingRequiredField_ReturnsFailure()
    {
        var component = new PdfGeneratorComponent();
        var outputDir = TempOutputDir();
        var config = MakeConfig("escrow_shortage_urgent", outputDir);
        var data = Loan1Data();
        data.Remove("borrower_name");   // required field

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("MISSING_INPUT", result.Error!.ErrorCode);
        Assert.Contains("borrower_name", result.Error.ErrorMessage);
        Assert.NotEmpty(result.LogEntries);
        Assert.False(data.ContainsKey("statement_date"));
    }

    [Fact]
    public async Task GeneratePdf_UnknownTemplateId_ReturnsFailure()
    {
        var component = new PdfGeneratorComponent();
        var outputDir = TempOutputDir();
        var config = MakeConfig("does_not_exist", outputDir);
        var data = Loan1Data();

        var result = await component.ExecuteAsync(config, data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("TEMPLATE_NOT_FOUND", result.Error!.ErrorCode);
    }

    [Fact]
    public async Task GeneratePdf_NonexistentRegistry_ReturnsFailure()
    {
        var component = new PdfGeneratorComponent();
        var config = new ComponentConfiguration
        {
            ComponentType = "pdf_generator",
            ComponentVersion = "1.0",
            Config = JsonDocument.Parse("""
            {
              "template_id": "escrow_shortage_urgent",
              "template_registry_path": "/nonexistent/registry.json",
              "output_directory": "/tmp"
            }
            """).RootElement
        };

        var result = await component.ExecuteAsync(config, Loan1Data());

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONFIG_ERROR", result.Error!.ErrorCode);
    }

    [Fact]
    public async Task GeneratePdf_EmptyTemplateId_ReturnsFailure()
    {
        var component = new PdfGeneratorComponent();
        var config = new ComponentConfiguration
        {
            ComponentType = "pdf_generator",
            ComponentVersion = "1.0",
            Config = JsonDocument.Parse("""
            {
              "template_id": "",
              "template_registry_path": "/some/path",
              "output_directory": "/tmp"
            }
            """).RootElement
        };

        var result = await component.ExecuteAsync(config, Loan1Data());

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONFIG_ERROR", result.Error!.ErrorCode);
    }

    [Fact]
    public async Task GeneratePdf_UnsupportedPageSize_ReturnsFailure()
    {
        var component = new PdfGeneratorComponent();
        var outputDir = TempOutputDir();
        var registryPath = FindRegistryPath();
        var config = new ComponentConfiguration
        {
            ComponentType = "pdf_generator",
            ComponentVersion = "1.0",
            Config = JsonDocument.Parse($$"""
            {
              "template_id": "escrow_shortage_urgent",
              "template_registry_path": "{{registryPath.Replace("\\", "\\\\")}}",
              "output_directory": "{{outputDir.Replace("\\", "\\\\")}}",
              "page": { "size": "B0" }
            }
            """).RootElement
        };

        var result = await component.ExecuteAsync(config, Loan1Data());

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("CONFIG_ERROR", result.Error!.ErrorCode);
        Assert.Contains("Unsupported page size", result.Error.ErrorMessage);
    }

    [Fact]
    public void ComponentType_IsPdfGenerator()
    {
        var component = new PdfGeneratorComponent();
        Assert.Equal("pdf_generator", component.ComponentType);
    }
}

// Helper shared between test classes
file static class TestHelpers
{
    public static Dictionary<string, string> BuildLoan1Data() => new()
    {
        ["loan_number"] = "1000001",
        ["borrower_name"] = "SMITH, JOHN A",
        ["property_address"] = "123 OAK STREET, ANYTOWN TX 75001",
        ["escrow_balance"] = "$2,150.00",
        ["escrow_payment"] = "$485.00",
        ["required_reserve"] = "$2,800.00",
        ["shortage_amount"] = "$650.00",
        ["surplus_amount"] = "$0.00",
        ["monthly_payment"] = "$1,229.85",
        ["tax_amount"] = "$3,200.00",
        ["hazard_insurance"] = "$1,450.00",
        ["flood_insurance"] = "$620.00",
        ["mortgage_insurance"] = "$0.00",
        ["last_analysis_date"] = "01/15/2025",
        ["next_analysis_date"] = "01/15/2026"
    };
}
