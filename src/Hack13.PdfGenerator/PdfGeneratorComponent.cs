using System.Diagnostics;
using System.Text.Json;
using PuppeteerSharp.Media;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Interfaces;
using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;

namespace Hack13.PdfGenerator;

public class PdfGeneratorComponent : IComponent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly Func<DateTimeOffset> _clock;

    public PdfGeneratorComponent()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    internal PdfGeneratorComponent(Func<DateTimeOffset> clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string ComponentType => "pdf_generator";

    public async Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var logs = new List<LogEntry>();
        var writtenKeys = new Dictionary<string, string>();
        var workingData = new Dictionary<string, string>(dataDictionary);

        try
        {
            var cfg = config.Config.Deserialize<PdfGeneratorConfig>(JsonOptions)
                      ?? throw new InvalidOperationException("PdfGenerator configuration is null.");

            // Resolve placeholders in config fields
            var templateId = PlaceholderResolver.Resolve(cfg.TemplateId, workingData);
            var registryPath = PlaceholderResolver.Resolve(cfg.TemplateRegistryPath, workingData);
            var outputDir = PlaceholderResolver.Resolve(cfg.OutputDirectory, workingData);

            if (string.IsNullOrWhiteSpace(templateId))
                return Failure("CONFIG_ERROR", "template_id is required.", null, logs, sw);
            if (string.IsNullOrWhiteSpace(registryPath))
                return Failure("CONFIG_ERROR", "template_registry_path is required.", null, logs, sw);
            if (string.IsNullOrWhiteSpace(outputDir))
                return Failure("CONFIG_ERROR", "output_directory is required.", null, logs, sw);

            logs.Add(MakeLog(LogLevel.Info, $"Using template_id='{templateId}'"));

            // Auto-inject statement_date if not present
            if (!workingData.ContainsKey("statement_date"))
                workingData["statement_date"] = _clock().UtcDateTime.ToString("yyyy-MM-dd");

            // Load template registry
            TemplateRegistry registry;
            try
            {
                registry = TemplateEngine.LoadRegistry(registryPath);
            }
            catch (FileNotFoundException ex)
            {
                return Failure("CONFIG_ERROR", ex.Message, "template_registry_path", logs, sw);
            }

            // Find template
            TemplateEntry entry;
            try
            {
                entry = TemplateEngine.FindTemplate(registry, templateId);
            }
            catch (KeyNotFoundException ex)
            {
                return Failure("TEMPLATE_NOT_FOUND", ex.Message, templateId, logs, sw);
            }

            // Validate required fields
            var missingFields = TemplateEngine.ValidateRequiredFields(entry, workingData);
            if (missingFields.Count > 0)
                return Failure("MISSING_INPUT",
                    $"Required field(s) missing: {string.Join(", ", missingFields)}",
                    missingFields[0], logs, sw);

            // Resolve template file path and load HTML
            string templateFilePath;
            string html;
            try
            {
                templateFilePath = TemplateEngine.ResolveRelativePath(registryPath, entry.FilePath);
                html = TemplateEngine.LoadTemplate(templateFilePath);
            }
            catch (InvalidOperationException ex)
            {
                return Failure("CONFIG_ERROR", ex.Message, entry.FilePath, logs, sw);
            }
            catch (FileNotFoundException ex)
            {
                return Failure("TEMPLATE_NOT_FOUND", ex.Message, entry.FilePath, logs, sw);
            }

            // Render HTML with data
            var renderedHtml = TemplateEngine.Render(html, workingData);
            logs.Add(MakeLog(LogLevel.Debug, "Template rendered successfully."));

            // Determine output filename
            var filenamePattern = cfg.FilenamePattern ?? entry.DefaultFilenamePattern;
            var filename = PlaceholderResolver.Resolve(filenamePattern, workingData);
            filename = SanitizeFilename(filename);

            // Ensure output directory exists
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, filename);

            // Build margin options
            var margins = cfg.Page?.Margins is { } m
                ? new MarginOptions { Top = m.Top, Bottom = m.Bottom, Left = m.Left, Right = m.Right }
                : null;
            PaperFormat paperFormat;
            try
            {
                paperFormat = ResolvePaperFormat(cfg.Page?.Size);
            }
            catch (InvalidOperationException ex)
            {
                return Failure("CONFIG_ERROR", ex.Message, "page.size", logs, sw);
            }

            // Generate PDF
            logs.Add(MakeLog(LogLevel.Info, "Starting PDF rendering..."));
            await using var renderer = new PdfRenderer();
            await renderer.EnsureBrowserAsync(cancellationToken);
            await renderer.RenderAsync(renderedHtml, outputPath, paperFormat, margins, cancellationToken);
            logs.Add(MakeLog(LogLevel.Info, $"PDF written to: {outputPath}"));

            // Collect output metadata
            var fileInfo = new FileInfo(outputPath);
            var pageCount = PdfRenderer.CountPages(outputPath);

            writtenKeys["pdf_file_path"] = outputPath;
            writtenKeys["pdf_file_name"] = filename;
            writtenKeys["pdf_file_size"] = fileInfo.Length.ToString();
            writtenKeys["pdf_page_count"] = pageCount.ToString();

            foreach (var (k, v) in writtenKeys)
                dataDictionary[k] = v;

            logs.Add(MakeLog(LogLevel.Info,
                $"PDF generation complete: {filename} ({fileInfo.Length:N0} bytes, {pageCount} page(s))"));

            return Success(writtenKeys, logs, sw);
        }
        catch (JsonException ex)
        {
            return Failure("CONFIG_ERROR", $"Invalid configuration: {ex.Message}", null, logs, sw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure("UNEXPECTED_ERROR", ex.Message, null, logs, sw);
        }
    }

    private static string SanitizeFilename(string filename)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(filename.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static ComponentResult Success(
        Dictionary<string, string> outputData, List<LogEntry> logs, Stopwatch sw) =>
        new()
        {
            Status = ComponentStatus.Success,
            OutputData = outputData,
            LogEntries = logs,
            DurationMs = sw.ElapsedMilliseconds
        };

    private static ComponentResult Failure(
        string code, string message, string? stepDetail, List<LogEntry> logs, Stopwatch sw) =>
        new()
        {
            Status = ComponentStatus.Failure,
            Error = new ComponentError { ErrorCode = code, ErrorMessage = message, StepDetail = stepDetail },
            LogEntries = logs,
            DurationMs = sw.ElapsedMilliseconds
        };

    private static PaperFormat ResolvePaperFormat(string? configuredSize)
    {
        if (string.IsNullOrWhiteSpace(configuredSize))
            return PaperFormat.Letter;

        return configuredSize.Trim().ToUpperInvariant() switch
        {
            "LETTER" => PaperFormat.Letter,
            "LEGAL" => PaperFormat.Legal,
            "A3" => PaperFormat.A3,
            "A4" => PaperFormat.A4,
            "A5" => PaperFormat.A5,
            _ => throw new InvalidOperationException($"Unsupported page size: '{configuredSize}'.")
        };
    }

    private static LogEntry MakeLog(LogLevel level, string message) =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            ComponentType = "pdf_generator",
            Level = level,
            Message = message
        };
}
