using System.Text.Json;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Models;

namespace Hack13.EmailSender.Tests;

public class EmailSenderComponentTests : IDisposable
{
    private readonly string _tempDir;

    public EmailSenderComponentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rpa-email-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ExecuteAsync_WithMockTransport_SendsEmailAndWritesOutputData()
    {
        var mockTransport = new MockTransport();
        var component = CreateComponent(mockTransport);
        var attachmentPath = CreatePdfFile();

        var data = new Dictionary<string, string>
        {
            ["customer_email"] = "customer@example.com",
            ["loan_number"] = "LN-123",
            ["borrower_name"] = "Taylor Lee",
            ["pdf_file_path"] = attachmentPath
        };

        var result = await component.ExecuteAsync(
            MakeConfig(
                subject: "Escrow Analysis Statement - Loan {{loan_number}}",
                body: "<p>Hello {{borrower_name}}</p>",
                attachments: ["{{pdf_file_path}}"]),
            data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("sent", result.OutputData["email_status"]);
        Assert.True(result.OutputData.ContainsKey("email_message_id"));
        Assert.True(result.OutputData.ContainsKey("email_sent_at"));
        Assert.Single(mockTransport.SentEmails);
        Assert.Equal("Escrow Analysis Statement - Loan LN-123", mockTransport.SentEmails[0].Subject);
        Assert.Equal("customer@example.com", mockTransport.SentEmails[0].To[0]);
        Assert.Equal(1, mockTransport.SentEmails[0].AttachmentCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingAttachment_FailsBeforeSend()
    {
        var mockTransport = new MockTransport();
        var component = CreateComponent(mockTransport);
        var missingPath = Path.Combine(_tempDir, "missing.pdf");

        var data = new Dictionary<string, string>
        {
            ["customer_email"] = "customer@example.com",
            ["pdf_file_path"] = missingPath
        };

        var result = await component.ExecuteAsync(
            MakeConfig(
                subject: "Test",
                body: "<p>Hello</p>",
                attachments: ["{{pdf_file_path}}"]),
            data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("ATTACHMENT_MISSING", result.Error?.ErrorCode);
        Assert.Equal("failed", result.OutputData["email_status"]);
        Assert.Empty(mockTransport.SentEmails);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidRecipient_FailsValidation()
    {
        var mockTransport = new MockTransport();
        var component = CreateComponent(mockTransport);

        var data = new Dictionary<string, string>
        {
            ["customer_email"] = "not-an-email"
        };

        var result = await component.ExecuteAsync(
            MakeConfig(subject: "Test", body: "<p>Hi</p>"),
            data);

        Assert.Equal(ComponentStatus.Failure, result.Status);
        Assert.Equal("INVALID_RECIPIENT", result.Error?.ErrorCode);
        Assert.Empty(mockTransport.SentEmails);
    }

    [Fact]
    public async Task ExecuteAsync_WithBodyTemplate_ResolvesPlaceholders()
    {
        var mockTransport = new MockTransport();
        var templatePath = Path.Combine(_tempDir, "escrow_email.html");
        await File.WriteAllTextAsync(templatePath, "<p>Dear {{borrower_name}} for loan {{loan_number}}</p>");

        var component = new EmailSenderComponent(
            mockTransport,
            new EmailSenderEnvironmentConfig { TemplateBasePath = _tempDir },
            () => new DateTimeOffset(2026, 02, 19, 12, 00, 00, TimeSpan.Zero));

        var data = new Dictionary<string, string>
        {
            ["customer_email"] = "customer@example.com",
            ["borrower_name"] = "Morgan",
            ["loan_number"] = "A-556"
        };

        var result = await component.ExecuteAsync(
            MakeConfig(subject: "Loan {{loan_number}}", bodyTemplate: "escrow_email.html"),
            data);

        Assert.Equal(ComponentStatus.Success, result.Status);
        Assert.Equal("sent", result.OutputData["email_status"]);
        Assert.Equal("2026-02-19T12:00:00.0000000+00:00", result.OutputData["email_sent_at"]);
        Assert.Single(mockTransport.SentEmails);
        Assert.Equal("Loan A-556", mockTransport.SentEmails[0].Subject);
    }

    private EmailSenderComponent CreateComponent(MockTransport mockTransport) =>
        new(
            mockTransport,
            new EmailSenderEnvironmentConfig { TemplateBasePath = _tempDir, AttachmentSizeLimitBytes = 10 * 1024 * 1024 },
            () => new DateTimeOffset(2026, 02, 19, 12, 00, 00, TimeSpan.Zero));

    private static ComponentConfiguration MakeConfig(
        string subject,
        string? body = null,
        string? bodyTemplate = null,
        List<string>? attachments = null) =>
        new()
        {
            ComponentType = "email_sender",
            ComponentVersion = "1.0",
            Config = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    from = "statements@example.com",
                    to = new[] { "{{customer_email}}" },
                    subject,
                    body,
                    body_template = bodyTemplate,
                    attachments = attachments ?? new List<string>(),
                    reply_to = "support@example.com"
                })).RootElement
        };

    private string CreatePdfFile()
    {
        var path = Path.Combine(_tempDir, "statement.pdf");
        File.WriteAllBytes(path, "%PDF-1.4\n%Mock".Select(c => (byte)c).ToArray());
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
