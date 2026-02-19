using Amazon;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using MimeKit;

namespace Hack13.EmailSender;

public sealed class SesTransportOptions
{
    public RegionEndpoint RegionEndpoint { get; init; } = RegionEndpoint.USEast1;
    public int MaxRetries { get; init; } = 3;
}

public sealed class SesTransport : IEmailTransport
{
    private readonly SesTransportOptions _options;
    private readonly IAmazonSimpleEmailServiceV2 _client;

    public SesTransport(SesTransportOptions options)
        : this(options, new AmazonSimpleEmailServiceV2Client(options?.RegionEndpoint ?? RegionEndpoint.USEast1))
    {
    }

    internal SesTransport(SesTransportOptions options, IAmazonSimpleEmailServiceV2 client)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<EmailSendResult> SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await message.WriteToAsync(ms, cancellationToken);
        ms.Position = 0;

        var request = new SendEmailRequest
        {
            Content = new EmailContent
            {
                Raw = new RawMessage
                {
                    Data = ms
                }
            }
        };

        var retries = Math.Max(0, _options.MaxRetries);
        var attempt = 0;

        while (true)
        {
            try
            {
                var response = await _client.SendEmailAsync(request, cancellationToken);
                return EmailSendResult.Sent(response.MessageId);
            }
            catch (TooManyRequestsException) when (attempt < retries)
            {
                attempt++;
                await Task.Delay(Backoff(attempt), cancellationToken);
            }
            catch (AmazonSimpleEmailServiceV2Exception ex) when (IsThrottling(ex) && attempt < retries)
            {
                attempt++;
                await Task.Delay(Backoff(attempt), cancellationToken);
            }
            catch (AmazonSimpleEmailServiceV2Exception ex)
            {
                var mappedCode = MapSesError(ex);
                return EmailSendResult.Failed(mappedCode, ex.Message, IsThrottling(ex));
            }
            catch (Exception ex)
            {
                return EmailSendResult.Failed("SES_SEND_FAILED", ex.Message);
            }
        }
    }

    private static bool IsThrottling(AmazonSimpleEmailServiceV2Exception ex) =>
        string.Equals(ex.ErrorCode, "ThrottlingException", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorCode, "TooManyRequestsException", StringComparison.OrdinalIgnoreCase);

    private static string MapSesError(AmazonSimpleEmailServiceV2Exception ex)
    {
        return ex.ErrorCode switch
        {
            "MailFromDomainNotVerifiedException" => "SES_UNVERIFIED_SENDER",
            "MessageRejected" => "SES_MESSAGE_REJECTED",
            "AccountSuspendedException" => "SES_ACCOUNT_SUSPENDED",
            "SendingPausedException" => "SES_SENDING_PAUSED",
            "ConfigurationSetDoesNotExistException" => "SES_CONFIG_SET_NOT_FOUND",
            "ThrottlingException" => "SES_THROTTLED",
            "TooManyRequestsException" => "SES_THROTTLED",
            _ => "SES_SEND_FAILED"
        };
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(4000, 250 * Math.Pow(2, attempt)));
}
