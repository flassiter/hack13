using Amazon;

namespace Hack13.EmailSender;

public static class EmailTransportFactory
{
    public static IEmailTransport Create(TransportConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.Type.Trim().ToLowerInvariant() switch
        {
            "ses" => new SesTransport(new SesTransportOptions
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(config.Ses.Region),
                MaxRetries = config.Ses.MaxRetries
            }),
            "smtp" => new SmtpTransport(new SmtpTransportOptions
            {
                Host = config.Smtp.Host,
                Port = config.Smtp.Port,
                UseSsl = config.Smtp.UseSsl,
                Username = config.Smtp.Username,
                Password = config.Smtp.Password
            }),
            "mock" => new MockTransport(),
            _ => throw new InvalidOperationException($"Unsupported email transport type: '{config.Type}'.")
        };
    }
}
