using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Hack13.EmailSender;

public sealed class SmtpTransportOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 25;
    public bool UseSsl { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
}

public sealed class SmtpTransport : IEmailTransport
{
    private readonly SmtpTransportOptions _options;

    public SmtpTransport(SmtpTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<EmailSendResult> SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new SmtpClient();
            var socketOptions = _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(_options.Username))
                await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);

            var smtpResponse = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            var messageId = ExtractMessageId(smtpResponse)
                ?? message.MessageId
                ?? $"smtp-{Guid.NewGuid():N}";

            return EmailSendResult.Sent(messageId);
        }
        catch (Exception ex)
        {
            return EmailSendResult.Failed("SMTP_SEND_FAILED", ex.Message);
        }
    }

    private static string? ExtractMessageId(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        // smtp4dev/MailHog often returns queue IDs in the first token.
        var token = response.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
