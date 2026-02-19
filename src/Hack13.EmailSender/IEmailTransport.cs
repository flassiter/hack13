using MimeKit;

namespace Hack13.EmailSender;

public interface IEmailTransport
{
    Task<EmailSendResult> SendAsync(MimeMessage message, CancellationToken cancellationToken);
}
