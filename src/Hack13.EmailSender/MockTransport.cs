using MimeKit;

namespace Hack13.EmailSender;

public sealed class MockTransport : IEmailTransport
{
    private readonly List<MockSentEmail> _sentEmails = [];

    public IReadOnlyList<MockSentEmail> SentEmails => _sentEmails;

    public Task<EmailSendResult> SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = string.IsNullOrWhiteSpace(message.MessageId)
            ? $"mock-{Guid.NewGuid():N}"
            : message.MessageId;

        _sentEmails.Add(new MockSentEmail
        {
            MessageId = messageId,
            Subject = message.Subject,
            From = message.From.Mailboxes.Select(x => x.Address).ToList(),
            To = message.To.Mailboxes.Select(x => x.Address).ToList(),
            Cc = message.Cc.Mailboxes.Select(x => x.Address).ToList(),
            Bcc = message.Bcc.Mailboxes.Select(x => x.Address).ToList(),
            AttachmentCount = (message.Body as Multipart)?.Count(x => x is MimePart) ?? 0
        });

        return Task.FromResult(EmailSendResult.Sent(messageId));
    }
}

public sealed class MockSentEmail
{
    public string MessageId { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public List<string> From { get; init; } = [];
    public List<string> To { get; init; } = [];
    public List<string> Cc { get; init; } = [];
    public List<string> Bcc { get; init; } = [];
    public int AttachmentCount { get; init; }
}
