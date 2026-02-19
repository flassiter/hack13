using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hack13.Contracts.Utilities;

namespace Hack13.PdfGenerator;

internal static class TemplateEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly Regex ConditionalBlockRegex = new(
        @"\{\{#if\s+(\w+)\}\}(.*?)\{\{/if\}\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex PlaceholderRegex = new(
        @"\{\{(\w+)(?::(\w+))?\}\}",
        RegexOptions.Compiled);

    public static TemplateRegistry LoadRegistry(string registryPath)
    {
        if (!File.Exists(registryPath))
            throw new FileNotFoundException($"Template registry not found: {registryPath}");

        var json = File.ReadAllText(registryPath);
        return JsonSerializer.Deserialize<TemplateRegistry>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to parse template registry: {registryPath}");
    }

    public static TemplateEntry FindTemplate(TemplateRegistry registry, string templateId)
    {
        return registry.Templates.FirstOrDefault(t =>
            string.Equals(t.TemplateId, templateId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Template '{templateId}' not found in registry.");
    }

    public static string LoadTemplate(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Template file not found: {filePath}");

        return File.ReadAllText(filePath);
    }

    public static string ResolveRelativePath(string registryPath, string templateFilePath)
    {
        var registryDir = Path.GetDirectoryName(Path.GetFullPath(registryPath))
                          ?? throw new InvalidOperationException("Cannot determine registry directory.");
        var resolvedPath = Path.IsPathRooted(templateFilePath)
            ? Path.GetFullPath(templateFilePath)
            : Path.GetFullPath(Path.Combine(registryDir, templateFilePath));

        if (!IsPathWithinDirectory(resolvedPath, registryDir))
        {
            throw new InvalidOperationException(
                $"Template file path escapes registry directory: {templateFilePath}");
        }

        return resolvedPath;
    }

    public static IReadOnlyList<string> ValidateRequiredFields(
        TemplateEntry template,
        IReadOnlyDictionary<string, string> data)
    {
        return template.RequiredFields
            .Where(f => !data.ContainsKey(f) || string.IsNullOrWhiteSpace(data[f]))
            .ToList();
    }

    public static string Render(string template, IReadOnlyDictionary<string, string> data)
    {
        var html = ProcessConditionalBlocks(template, data);
        return ReplacePlaceholders(html, data);
    }

    private static string ProcessConditionalBlocks(string template, IReadOnlyDictionary<string, string> data)
    {
        return ConditionalBlockRegex.Replace(template, match =>
        {
            var fieldName = match.Groups[1].Value;
            var content = match.Groups[2].Value;

            if (!data.TryGetValue(fieldName, out var value) || IsZeroOrEmpty(value))
                return string.Empty;

            return content;
        });
    }

    private static bool IsZeroOrEmpty(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (NumericParser.TryParse(value, out var num) && num == 0m) return true;
        return false;
    }

    private static string ReplacePlaceholders(string template, IReadOnlyDictionary<string, string> data)
    {
        return PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            var format = match.Groups[2].Success ? match.Groups[2].Value : null;

            if (!data.TryGetValue(key, out var value))
                return string.Empty;

            var formattedValue = ApplyFormat(value, format);
            return string.Equals(format, "raw", StringComparison.OrdinalIgnoreCase)
                ? formattedValue
                : HtmlEncoder.Default.Encode(formattedValue);
        });
    }

    private static string ApplyFormat(string value, string? format)
    {
        if (format == null) return value;

        return format.ToLowerInvariant() switch
        {
            "currency" => FormatCurrency(value),
            "date" => FormatDate(value),
            "uppercase" => value.ToUpperInvariant(),
            "lowercase" => value.ToLowerInvariant(),
            "raw" => value,
            _ => value
        };
    }

    private static string FormatCurrency(string value)
    {
        if (NumericParser.TryParse(value, out var num))
            return num.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        return value;
    }

    private static string FormatDate(string value)
    {
        string[] formats = ["MM/dd/yyyy", "yyyy-MM-dd", "M/d/yyyy", "yyyy/MM/dd"];
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        if (DateTime.TryParse(value, out date))
            return date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        return value;
    }

    private static bool IsPathWithinDirectory(string candidatePath, string directoryPath)
    {
        var fullDirectoryPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
        var fullCandidatePath = Path.GetFullPath(candidatePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return fullCandidatePath.StartsWith(fullDirectoryPath, comparison);
    }
}
