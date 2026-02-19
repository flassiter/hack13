using System.Diagnostics;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using MimeKit;
using MimeKit.Utils;
using Hack13.Contracts.Enums;
using Hack13.Contracts.Interfaces;
using Hack13.Contracts.Models;
using Hack13.Contracts.Utilities;

namespace Hack13.EmailSender;

public sealed class EmailSenderComponent : IComponent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    private readonly IEmailTransport _transport;
    private readonly EmailSenderEnvironmentConfig _environmentConfig;
    private readonly Func<DateTimeOffset> _clock;

    public EmailSenderComponent()
        : this(new MockTransport(), new EmailSenderEnvironmentConfig(), () => DateTimeOffset.UtcNow)
    {
    }

    public EmailSenderComponent(
        IEmailTransport transport,
        EmailSenderEnvironmentConfig? environmentConfig = null,
        Func<DateTimeOffset>? clock = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _environmentConfig = environmentConfig ?? new EmailSenderEnvironmentConfig();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string ComponentType => "email_sender";

    public async Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var logs = new List<LogEntry>();

        try
        {
            var emailConfig = config.Config.Deserialize<EmailSenderConfig>(JsonOptions)
                ?? throw new InvalidOperationException("Email sender configuration is null.");

            if (string.IsNullOrWhiteSpace(emailConfig.From))
                return Failure("CONFIG_ERROR", "from is required.", "from", logs, sw);

            var from = ResolveAndValidateAddress(emailConfig.From, dataDictionary, "from", "FROM_INVALID");
            if (!from.IsValid)
                return Failure(from.ErrorCode!, from.ErrorMessage!, "from", logs, sw);

            var to = ResolveAndValidateAddresses(emailConfig.To, dataDictionary, "to");
            if (!to.IsValid)
                return Failure(to.ErrorCode!, to.ErrorMessage!, "to", logs, sw);
            if (to.Parsed.Count == 0)
                return Failure("CONFIG_ERROR", "At least one recipient in 'to' is required.", "to", logs, sw);

            var cc = ResolveAndValidateAddresses(emailConfig.Cc, dataDictionary, "cc");
            if (!cc.IsValid)
                return Failure(cc.ErrorCode!, cc.ErrorMessage!, "cc", logs, sw);

            var bcc = ResolveAndValidateAddresses(emailConfig.Bcc, dataDictionary, "bcc");
            if (!bcc.IsValid)
                return Failure(bcc.ErrorCode!, bcc.ErrorMessage!, "bcc", logs, sw);

            var replyTo = string.IsNullOrWhiteSpace(emailConfig.ReplyTo)
                ? (IsValid: true, Address: (MailboxAddress?)null, ErrorCode: (string?)null, ErrorMessage: (string?)null)
                : ResolveAndValidateAddress(emailConfig.ReplyTo, dataDictionary, "reply_to", "REPLY_TO_INVALID");
            if (!replyTo.IsValid)
                return Failure(replyTo.ErrorCode!, replyTo.ErrorMessage!, "reply_to", logs, sw);

            var subject = PlaceholderResolver.Resolve(emailConfig.Subject ?? string.Empty, dataDictionary);
            if (string.IsNullOrWhiteSpace(subject))
                return Failure("CONFIG_ERROR", "subject is required after placeholder resolution.", "subject", logs, sw);

            var htmlBody = await ResolveBodyAsync(emailConfig, dataDictionary, cancellationToken);
            if (string.IsNullOrWhiteSpace(htmlBody))
                return Failure("CONFIG_ERROR", "body or body_template is required.", "body", logs, sw);

            var attachmentValidation = ResolveAndValidateAttachments(emailConfig.Attachments, dataDictionary);
            if (!attachmentValidation.IsValid)
                return Failure(attachmentValidation.ErrorCode!, attachmentValidation.ErrorMessage!, "attachments", logs, sw);

            var message = BuildMessage(
                from.Address!,
                to.Parsed,
                cc.Parsed,
                bcc.Parsed,
                replyTo.Address,
                subject,
                htmlBody,
                attachmentValidation.Paths);

            var sendResult = await _transport.SendAsync(message, cancellationToken);
            if (!sendResult.IsSuccess)
            {
                return Failure(
                    sendResult.ErrorCode ?? "SEND_FAILED",
                    sendResult.ErrorMessage ?? "Email send failed.",
                    null,
                    logs,
                    sw,
                    outputData: new Dictionary<string, string>
                    {
                        ["email_status"] = "failed"
                    });
            }

            var sentAt = _clock().ToString("O");
            var outputData = new Dictionary<string, string>
            {
                ["email_message_id"] = sendResult.MessageId ?? string.Empty,
                ["email_status"] = "sent",
                ["email_sent_at"] = sentAt
            };

            foreach (var (key, value) in outputData)
                dataDictionary[key] = value;

            logs.Add(MakeLog(LogLevel.Info,
                $"Email sent to {to.Parsed.Count} recipient(s), attachment_count={attachmentValidation.Paths.Count}."));

            return Success(outputData, logs, sw);
        }
        catch (JsonException ex)
        {
            return Failure("CONFIG_ERROR", $"Invalid email sender configuration: {ex.Message}", null, logs, sw);
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

    private async Task<string> ResolveBodyAsync(
        EmailSenderConfig emailConfig,
        IReadOnlyDictionary<string, string> dataDictionary,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(emailConfig.BodyTemplate))
        {
            var resolvedTemplateName = PlaceholderResolver.Resolve(emailConfig.BodyTemplate, dataDictionary);
            var fullPath = Path.Combine(_environmentConfig.TemplateBasePath, resolvedTemplateName);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Email body template not found: {fullPath}");

            var templateContent = await File.ReadAllTextAsync(fullPath, cancellationToken);
            return PlaceholderResolver.Resolve(templateContent, dataDictionary);
        }

        var body = emailConfig.Body ?? string.Empty;
        return PlaceholderResolver.Resolve(body, dataDictionary);
    }

    private (bool IsValid, string? ErrorCode, string? ErrorMessage, List<string> Paths) ResolveAndValidateAttachments(
        IEnumerable<string> configuredAttachments,
        IReadOnlyDictionary<string, string> dataDictionary)
    {
        var resolvedPaths = new List<string>();
        foreach (var raw in configuredAttachments)
        {
            var path = PlaceholderResolver.Resolve(raw, dataDictionary).Trim();
            if (string.IsNullOrWhiteSpace(path))
                return (false, "ATTACHMENT_INVALID", "Attachment path is empty after placeholder resolution.", []);

            if (!File.Exists(path))
                return (false, "ATTACHMENT_MISSING", $"Attachment not found: {path}", []);

            var info = new FileInfo(path);
            if (info.Length > _environmentConfig.AttachmentSizeLimitBytes)
            {
                return (
                    false,
                    "ATTACHMENT_TOO_LARGE",
                    $"Attachment exceeds size limit ({_environmentConfig.AttachmentSizeLimitBytes} bytes): {path}",
                    []);
            }

            resolvedPaths.Add(path);
        }

        return (true, null, null, resolvedPaths);
    }

    private static (bool IsValid, string? ErrorCode, string? ErrorMessage, List<MailboxAddress> Parsed) ResolveAndValidateAddresses(
        IEnumerable<string> configuredAddresses,
        IReadOnlyDictionary<string, string> dataDictionary,
        string fieldName)
    {
        var parsed = new List<MailboxAddress>();

        foreach (var raw in configuredAddresses)
        {
            var resolved = PlaceholderResolver.Resolve(raw, dataDictionary).Trim();
            if (!TryParseValidatedMailbox(resolved, out var mailbox))
            {
                return (false, "INVALID_RECIPIENT", $"Invalid {fieldName} email address: '{resolved}'", []);
            }

            parsed.Add(mailbox);
        }

        return (true, null, null, parsed);
    }

    private static (bool IsValid, MailboxAddress? Address, string? ErrorCode, string? ErrorMessage) ResolveAndValidateAddress(
        string configuredAddress,
        IReadOnlyDictionary<string, string> dataDictionary,
        string fieldName,
        string errorCode)
    {
        var resolved = PlaceholderResolver.Resolve(configuredAddress, dataDictionary).Trim();
        if (!TryParseValidatedMailbox(resolved, out var mailbox))
            return (false, null, errorCode, $"Invalid {fieldName} email address: '{resolved}'");

        return (true, mailbox, null, null);
    }

    private static MimeMessage BuildMessage(
        MailboxAddress from,
        IReadOnlyCollection<MailboxAddress> to,
        IReadOnlyCollection<MailboxAddress> cc,
        IReadOnlyCollection<MailboxAddress> bcc,
        MailboxAddress? replyTo,
        string subject,
        string htmlBody,
        IReadOnlyCollection<string> attachmentPaths)
    {
        var message = new MimeMessage();
        message.From.Add(from);
        message.To.AddRange(to);
        message.Cc.AddRange(cc);
        message.Bcc.AddRange(bcc);
        if (replyTo != null)
            message.ReplyTo.Add(replyTo);
        message.Subject = subject;
        message.MessageId = MimeUtils.GenerateMessageId();

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = ConvertHtmlToPlainText(htmlBody)
        };

        foreach (var path in attachmentPaths)
            bodyBuilder.Attachments.Add(path);

        message.Body = bodyBuilder.ToMessageBody();
        return message;
    }

    private static string ConvertHtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var noTags = HtmlTagRegex.Replace(html, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static bool TryParseValidatedMailbox(string input, out MailboxAddress mailbox)
    {
        mailbox = null!;

        if (!MailboxAddress.TryParse(input, out var parsed))
            return false;

        try
        {
            var mailAddress = new MailAddress(parsed.Address);
            if (string.IsNullOrWhiteSpace(mailAddress.Host))
                return false;
            if (!parsed.Address.Contains('@', StringComparison.Ordinal))
                return false;

            mailbox = parsed;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static ComponentResult Success(
        Dictionary<string, string> outputData,
        List<LogEntry> logs,
        Stopwatch sw) =>
        new()
        {
            Status = ComponentStatus.Success,
            OutputData = outputData,
            LogEntries = logs,
            DurationMs = sw.ElapsedMilliseconds
        };

    private static ComponentResult Failure(
        string code,
        string message,
        string? stepDetail,
        List<LogEntry> logs,
        Stopwatch sw,
        Dictionary<string, string>? outputData = null) =>
        new()
        {
            Status = ComponentStatus.Failure,
            Error = new ComponentError { ErrorCode = code, ErrorMessage = message, StepDetail = stepDetail },
            LogEntries = logs,
            OutputData = outputData ?? new Dictionary<string, string>
            {
                ["email_status"] = "failed"
            },
            DurationMs = sw.ElapsedMilliseconds
        };

    private static LogEntry MakeLog(LogLevel level, string message) =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            ComponentType = "email_sender",
            Level = level,
            Message = message
        };
}
